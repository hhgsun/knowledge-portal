using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using KnowledgePortal.Api.Tests.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

/// <summary>
/// Verifies attachment text extraction for the OpenXML formats that feed the search and
/// embedding index. Files are generated on disk and read back through the production extractor.
/// </summary>
public class AttachmentTextExtractorTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly List<string> _tempDirectories = [];

    private string TempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kp_extract_{Guid.NewGuid():N}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
        foreach (var directory in _tempDirectories.OrderByDescending(x => x.Length))
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // ── .xlsx (OpenXML) — the format that was uploadable but never indexed ──

    [Fact]
    public void ExtractText_Xlsx_ReturnsSharedAndInlineCellText()
    {
        var path = TempPath(".xlsx");
        CreateXlsx(path, sharedString: "MutabakatRaporu", directString: "42000TL");

        var text = AttachmentTextExtractor.ExtractText(path, ".xlsx");

        Assert.Contains("MutabakatRaporu", text);
        Assert.Contains("42000TL", text);
        Assert.Contains("| MutabakatRaporu | 42000TL |", text);
        Assert.Contains("| --- | --- |", text);
    }

    [Fact]
    public void Extract_DocxTable_PreservesRowsAndColumnsAsMarkdown()
    {
        var path = TempPath(".docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var table = new DocumentFormat.OpenXml.Wordprocessing.Table(
                WordRow("Kod", "Açıklama"), WordRow("ERR42", "Sertifika geçersiz"));
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
                new DocumentFormat.OpenXml.Wordprocessing.Body(table));
            main.Document.Save();
        }

        var result = AttachmentTextExtractor.Extract(path, ".docx");

        Assert.Equal(1, result.TableCount);
        Assert.Contains("| Kod | Açıklama |", result.Text);
        Assert.Contains("| ERR42 | Sertifika geçersiz |", result.Text);
        Assert.Equal("table", Assert.Single(result.Segments).Kind);
    }

    [Fact]
    public void Extract_Csv_HandlesQuotedCommasAndProducesMarkdown()
    {
        var path = TempPath(".csv");
        File.WriteAllText(path, "Kod,Açıklama\nERR42,\"Sertifika,\ngeçersiz\"");

        var result = AttachmentTextExtractor.Extract(path, ".csv");

        Assert.Contains("| Kod | Açıklama |", result.Text);
        Assert.Contains("| ERR42 | Sertifika,<br>geçersiz |", result.Text);
        Assert.Equal(1, result.TableCount);
    }

    [Fact]
    public async Task Processing_Image_UsesVisionOnceAndCachesSearchableDescription()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kp_visual_{Guid.NewGuid():N}");
        _tempDirectories.Add(root);
        var articleDirectory = Path.Combine(root, "article-1");
        Directory.CreateDirectory(articleDirectory);
        var path = Path.Combine(articleDirectory, "visual.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        _tempFiles.Add(path);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:BasePath"] = root,
            ["Ollama:Enabled"] = "true",
            ["Ollama:ChatModel"] = "qwen2.5vl:7b",
            ["DocumentParsing:Vision:Enabled"] = "true"
        }).Build();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var fake = new FakeChatClient { ResponseOverride = "## Açıklama\nVPN ağ şeması\n\nOCR: ERR42" };
        using var provider = new ServiceCollection().AddSingleton<IChatClient>(fake).BuildServiceProvider();
        var service = new AttachmentProcessingService(db, config, provider, new HttpClient(),
            NullLogger<AttachmentProcessingService>.Instance);
        var attachment = new ArticleAttachment
        {
            Id = "att-1", ArticleId = "article-1", FileName = "diagram.png",
            StoredFileName = "visual.png", ContentType = "image/png", Sha256 = "hash",
            UploadedById = "user"
        };

        var first = await service.PrepareAsync(attachment);
        var second = await service.PrepareAsync(attachment);

        Assert.Equal("completed", first.Status);
        Assert.Equal(1, first.VisualCount);
        Assert.Contains("VPN ağ şeması", first.Text);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(1, fake.CallCount);
    }

    // ── graceful degradation ──

    [Fact]
    public void ExtractText_UnsupportedExtension_ReturnsEmpty()
    {
        var path = TempPath(".zip");
        File.WriteAllText(path, "not extractable");

        Assert.Equal("", AttachmentTextExtractor.ExtractText(path, ".zip"));
    }

    [Fact]
    public void ExtractText_MissingFile_ReturnsEmpty()
    {
        var result = AttachmentTextExtractor.Extract(TempPath(".pdf"), ".pdf");
        Assert.Equal("", result.Text);
        Assert.Equal("failed", result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ExtractText_CorruptOfficeFile_ReturnsEmptyNotThrow()
    {
        var path = TempPath(".xlsx");
        File.WriteAllText(path, "definitely not a spreadsheet");

        var result = AttachmentTextExtractor.Extract(path, ".xlsx");
        Assert.Equal("", result.Text);
        Assert.Equal("failed", result.Status);
    }

    [Fact]
    public void Extract_LongText_TruncatesAtConfiguredLimitAndReportsIt()
    {
        var path = TempPath(".txt");
        File.WriteAllText(path, new string('a', 1_500));

        var result = AttachmentTextExtractor.Extract(path, ".txt", 1_000);

        Assert.Equal("completed", result.Status);
        Assert.True(result.Truncated);
        Assert.Equal(1_000, result.Text.Length);
        Assert.Equal(1_000, result.ExtractedCharacters);
        Assert.Equal(1_000, result.CharacterLimit);
    }

    [Fact]
    public void Extract_ShortText_ReportsCompleteWithoutTruncation()
    {
        var path = TempPath(".md");
        File.WriteAllText(path, "# Başlık\n\nKısa içerik");

        var result = AttachmentTextExtractor.Extract(path, ".md", 1_000);

        Assert.Equal("completed", result.Status);
        Assert.False(result.Truncated);
        Assert.Equal(result.Text.Length, result.ExtractedCharacters);
        Assert.Equal(1_000, result.CharacterLimit);
    }

    private static void CreateXlsx(string path, string sharedString, string directString)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wbPart = doc.AddWorkbookPart();
        wbPart.Workbook = new Workbook();

        var sstPart = wbPart.AddNewPart<SharedStringTablePart>();
        sstPart.SharedStringTable = new SharedStringTable(new SharedStringItem(new Text(sharedString)));

        var wsPart = wbPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        wsPart.Worksheet = new Worksheet(sheetData);

        var sheets = wbPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "Sheet1" });

        var row = new Row();
        row.Append(new Cell { DataType = CellValues.SharedString, CellValue = new CellValue("0") });
        row.Append(new Cell { DataType = CellValues.String, CellValue = new CellValue(directString) });
        sheetData.Append(row);

        wbPart.Workbook.Save();
    }

    private static DocumentFormat.OpenXml.Wordprocessing.TableRow WordRow(params string[] values) =>
        new(values.Select(value => new DocumentFormat.OpenXml.Wordprocessing.TableCell(
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text(value))))));
}
