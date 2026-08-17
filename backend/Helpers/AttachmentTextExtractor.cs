using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;

namespace KnowledgePortal.Api.Helpers;

public sealed record AttachmentTextSegment(string Text, string Location);
public sealed record AttachmentExtractionResult(string Status, string Text,
    IReadOnlyList<AttachmentTextSegment> Segments, string? Error = null);

public static class AttachmentTextExtractor
{
    private const int MaxCharacters = 50_000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".yaml", ".yml"
    };

    /// <summary>
    /// Backwards-compatible text-only projection. Call <see cref="Extract"/> when status and
    /// provenance are required.
    /// </summary>
    public static string ExtractText(string filePath, string extension) => Extract(filePath, extension).Text;

    public static AttachmentExtractionResult Extract(string filePath, string extension)
    {
        try
        {
            if (!File.Exists(filePath))
                return new("failed", "", [], "File is missing from attachment storage");

            var ext = extension.ToLowerInvariant();

            var segments = ext switch
            {
                ".pdf" => ExtractFromPdf(filePath),
                ".docx" => ExtractFromDocx(filePath),
                ".xlsx" => ExtractFromXlsx(filePath),
                ".pptx" => ExtractFromPptx(filePath),
                _ when TextExtensions.Contains(ext) => ExtractFromTextFile(filePath),
                _ => []
            };

            var normalized = LimitSegments(segments);
            var text = string.Join(' ', normalized.Select(x => x.Text)).Trim();
            return new(string.IsNullOrWhiteSpace(text) ? "no_text" : "completed", text, normalized);
        }
        catch (Exception ex)
        {
            return new("failed", "", [], ex.Message[..Math.Min(2000, ex.Message.Length)]);
        }
    }

    private static List<AttachmentTextSegment> ExtractFromPdf(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        return document.GetPages()
            .Select(page => new AttachmentTextSegment(page.Text, $"page:{page.Number}"))
            .Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
    }

    private static List<AttachmentTextSegment> ExtractFromDocx(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var doc = WordprocessingDocument.Open(stream, false);

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return [];

        var sb = new StringBuilder();
        foreach (var paragraph in body.Elements<Paragraph>())
        {
            sb.Append(paragraph.InnerText);
            sb.Append(' ');

            if (sb.Length > MaxCharacters) break;
        }

        return [new(sb.ToString(), "document")];
    }

    // ── Modern Office (OpenXML) — no extra dependency ──

    private static List<AttachmentTextSegment> ExtractFromXlsx(string filePath)
    {
        using var doc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart == null) return [];

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var segments = new List<AttachmentTextSegment>();

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var sb = new StringBuilder();
            foreach (var cell in worksheetPart.Worksheet.Descendants<Cell>())
            {
                var value = GetCellText(cell, sharedStrings);
                if (!string.IsNullOrEmpty(value))
                {
                    sb.Append(value);
                    sb.Append(' ');
                }

                if (sb.Length > MaxCharacters) break;
            }
            var sheetName = workbookPart.Workbook.Sheets?.Elements<Sheet>()
                .FirstOrDefault(s => s.Id?.Value == workbookPart.GetIdOfPart(worksheetPart))?.Name?.Value;
            if (sb.Length > 0) segments.Add(new(sb.ToString(), $"sheet:{sheetName ?? (segments.Count + 1).ToString()}"));
        }

        return segments;
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

    private static List<AttachmentTextSegment> ExtractFromPptx(string filePath)
    {
        using var doc = PresentationDocument.Open(filePath, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart == null) return [];

        var segments = new List<AttachmentTextSegment>();
        foreach (var (slidePart, index) in presentationPart.SlideParts.Select((part, index) => (part, index)))
        {
            var sb = new StringBuilder();
            // a:t (DrawingML Text) elements carry the run text inside every shape/table on a slide.
            foreach (var text in slidePart.Slide.Descendants<A.Text>())
            {
                sb.Append(text.Text);
                sb.Append(' ');

                if (sb.Length > MaxCharacters) break;
            }
            if (sb.Length > 0) segments.Add(new(sb.ToString(), $"slide:{index + 1}"));
        }

        return segments;
    }

    private static List<AttachmentTextSegment> ExtractFromTextFile(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var buffer = new char[MaxCharacters];
        var read = reader.Read(buffer, 0, MaxCharacters);
        var text = new string(buffer, 0, read);
        return string.IsNullOrWhiteSpace(text) ? [] : [new(text, "file")];
    }

    private static List<AttachmentTextSegment> LimitSegments(IEnumerable<AttachmentTextSegment> segments)
    {
        var result = new List<AttachmentTextSegment>();
        var remaining = MaxCharacters;
        foreach (var segment in segments)
        {
            if (remaining <= 0) break;
            var text = segment.Text.Trim();
            if (text.Length == 0) continue;
            if (text.Length > remaining) text = text[..remaining];
            result.Add(segment with { Text = text });
            remaining -= text.Length;
        }
        return result;
    }
}
