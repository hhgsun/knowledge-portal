using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Tests.Unit;

/// <summary>
/// Verifies attachment text extraction for the Office formats — especially .xlsx (OpenXML) and
/// the legacy .xls (NPOI) paths, which feed the search / embedding index. Files are generated on
/// disk with the same libraries and read back through the production extractor.
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

    // ── legacy .xls (NPOI) ──

    [Fact]
    public void ExtractText_LegacyXls_ReturnsCellText()
    {
        var path = TempPath(".xls");
        var wb = new NPOI.HSSF.UserModel.HSSFWorkbook();
        var sheet = wb.CreateSheet("Sayfa1");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("EskiExcelIcerigi");
        using (var fs = File.Create(path)) wb.Write(fs, false);
        wb.Close();

        var text = AttachmentTextExtractor.ExtractText(path, ".xls");

        Assert.Contains("EskiExcelIcerigi", text);
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
        Assert.Equal("", AttachmentTextExtractor.ExtractText(TempPath(".pdf"), ".pdf"));
    }

    [Fact]
    public void ExtractText_CorruptOfficeFile_ReturnsEmptyNotThrow()
    {
        // A .xls that isn't a real OLE2 document must be swallowed, not crash the indexer.
        var path = TempPath(".xls");
        File.WriteAllText(path, "definitely not a spreadsheet");

        Assert.Equal("", AttachmentTextExtractor.ExtractText(path, ".xls"));
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
