using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;

namespace KnowledgePortal.Api.Helpers;

public static class AttachmentTextExtractor
{
    private const int MaxCharacters = 50_000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".yaml", ".yml"
    };

    /// <summary>
    /// Extracts searchable text from a file. Returns empty string for unsupported or unreadable files.
    /// </summary>
    public static string ExtractText(string filePath, string extension)
    {
        try
        {
            if (!File.Exists(filePath)) return "";

            var ext = extension.ToLowerInvariant();

            var text = ext switch
            {
                ".pdf" => ExtractFromPdf(filePath),
                ".docx" => ExtractFromDocx(filePath),
                ".xlsx" => ExtractFromXlsx(filePath),
                ".pptx" => ExtractFromPptx(filePath),
                ".xls" => ExtractFromXls(filePath),
                _ when TextExtensions.Contains(ext) => ExtractFromTextFile(filePath),
                _ => ""
            };

            if (text.Length > MaxCharacters)
                text = text[..MaxCharacters];

            return text.Trim();
        }
        catch
        {
            // Corrupted/unreadable file — skip silently
            return "";
        }
    }

    private static string ExtractFromPdf(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            sb.Append(page.Text);
            sb.Append(' ');

            if (sb.Length > MaxCharacters) break;
        }

        return sb.ToString();
    }

    private static string ExtractFromDocx(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var doc = WordprocessingDocument.Open(stream, false);

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";

        var sb = new StringBuilder();
        foreach (var paragraph in body.Elements<Paragraph>())
        {
            sb.Append(paragraph.InnerText);
            sb.Append(' ');

            if (sb.Length > MaxCharacters) break;
        }

        return sb.ToString();
    }

    // ── Modern Office (OpenXML) — no extra dependency ──

    private static string ExtractFromXlsx(string filePath)
    {
        using var doc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart == null) return "";

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            foreach (var cell in worksheetPart.Worksheet.Descendants<Cell>())
            {
                var value = GetCellText(cell, sharedStrings);
                if (!string.IsNullOrEmpty(value))
                {
                    sb.Append(value);
                    sb.Append(' ');
                }

                if (sb.Length > MaxCharacters) return sb.ToString();
            }
        }

        return sb.ToString();
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.CellValue == null && cell.InlineString == null) return "";

        // Shared strings are stored once and referenced by index from each cell.
        if (cell.DataType?.Value == CellValues.SharedString
            && sharedStrings != null
            && int.TryParse(cell.CellValue?.InnerText, out var idx)
            && idx >= 0 && idx < sharedStrings.ChildElements.Count)
        {
            return sharedStrings.ChildElements[idx].InnerText;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText ?? "";

        return cell.CellValue?.InnerText ?? "";
    }

    private static string ExtractFromPptx(string filePath)
    {
        using var doc = PresentationDocument.Open(filePath, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart == null) return "";

        var sb = new StringBuilder();
        foreach (var slidePart in presentationPart.SlideParts)
        {
            // a:t (DrawingML Text) elements carry the run text inside every shape/table on a slide.
            foreach (var text in slidePart.Slide.Descendants<A.Text>())
            {
                sb.Append(text.Text);
                sb.Append(' ');

                if (sb.Length > MaxCharacters) return sb.ToString();
            }
        }

        return sb.ToString();
    }

    // ── Legacy binary Excel (OLE2) via NPOI. NPOI 2.8 ships HSSF (.xls) but not HWPF (.doc),
    //    so legacy Word is intentionally unsupported rather than uploadable-but-unindexed. ──

    private static string ExtractFromXls(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook(stream);
        var extractor = new NPOI.HSSF.Extractor.ExcelExtractor(workbook);
        return extractor.Text ?? "";
    }

    private static string ExtractFromTextFile(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var buffer = new char[MaxCharacters];
        var read = reader.Read(buffer, 0, MaxCharacters);
        return new string(buffer, 0, read);
    }
}
