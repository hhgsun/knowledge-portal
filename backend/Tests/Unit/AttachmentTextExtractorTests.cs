using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Tests.Unit;

/// <summary>
/// Verifies attachment text extraction for the OpenXML formats that feed the search and
/// embedding index. Files are generated on disk and read back through the production extractor.
/// </summary>
public class AttachmentTextExtractorTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

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
}
