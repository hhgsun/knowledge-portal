using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.VisualBasic.FileIO;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace KnowledgePortal.Api.Helpers;

public sealed record AttachmentTextSegment(string Text, string Location, string Kind = "text");
public sealed record AttachmentVisualAsset(ReadOnlyMemory<byte> Data, string MediaType,
    string Location, string? AltText = null);
public sealed record AttachmentExtractionResult(string Status, string Text,
    IReadOnlyList<AttachmentTextSegment> Segments, string? Error = null,
    bool Truncated = false, int ExtractedCharacters = 0, int CharacterLimit = 50_000,
    int TableCount = 0, int VisualCount = 0, string ExtractionProfile = "native-structured-v2");

/// <summary>
/// Local, deterministic extraction. Office tables are emitted as GFM Markdown, layout provenance
/// remains page/sheet/slide scoped, and visual assets are exposed separately for optional VLM/OCR
/// enrichment. Complex/scanned PDFs can be routed to the optional external parser service.
/// </summary>
public static class AttachmentTextExtractor
{
    internal const int DefaultMaxCharacters = 50_000;
    internal const string NativeProfile = "native-structured-v2";

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".yaml", ".yml"
    };
    private static readonly Dictionary<string, string> ImageMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp"
    };

    public static string ExtractText(string filePath, string extension) => Extract(filePath, extension).Text;

    public static AttachmentExtractionResult Extract(string filePath, string extension,
        int maxCharacters = DefaultMaxCharacters)
    {
        maxCharacters = Math.Clamp(maxCharacters, 1_000, 5_000_000);
        var readLimit = maxCharacters + 1;
        try
        {
            if (!File.Exists(filePath))
                return new("failed", "", [], "File is missing from attachment storage",
                    CharacterLimit: maxCharacters, ExtractionProfile: NativeProfile);

            var ext = extension.ToLowerInvariant();
            var segments = ext switch
            {
                ".pdf" => ExtractFromPdf(filePath, readLimit),
                ".docx" => ExtractFromDocx(filePath, readLimit),
                ".xlsx" => ExtractFromXlsx(filePath, readLimit),
                ".pptx" => ExtractFromPptx(filePath, readLimit),
                ".csv" => ExtractFromDelimited(filePath, readLimit),
                ".svg" => ExtractFromSvg(filePath, readLimit),
                _ when TextExtensions.Contains(ext) => ExtractFromTextFile(filePath, readLimit),
                _ => []
            };

            var (normalized, truncated) = LimitSegments(segments, maxCharacters);
            var text = string.Join("\n\n", normalized.Select(x => x.Text)).Trim();
            return new(string.IsNullOrWhiteSpace(text) ? "no_text" : "completed", text, normalized,
                Truncated: truncated, ExtractedCharacters: normalized.Sum(x => x.Text.Length),
                CharacterLimit: maxCharacters,
                TableCount: normalized.Count(x => x.Kind is "table" or "mixed-table"),
                VisualCount: normalized.Count(x => x.Kind == "image"),
                ExtractionProfile: NativeProfile);
        }
        catch (Exception ex)
        {
            return new("failed", "", [], ex.Message[..Math.Min(2000, ex.Message.Length)],
                CharacterLimit: maxCharacters, ExtractionProfile: NativeProfile);
        }
    }

    public static IReadOnlyList<AttachmentVisualAsset> ExtractVisualAssets(string filePath,
        string extension, int maxAssets = 12, int maxBytesPerAsset = 8 * 1024 * 1024)
    {
        if (!File.Exists(filePath) || maxAssets <= 0) return [];
        var ext = extension.ToLowerInvariant();
        try
        {
            if (ImageMediaTypes.TryGetValue(ext, out var mediaType))
            {
                var bytes = File.ReadAllBytes(filePath);
                return bytes.Length <= maxBytesPerAsset
                    ? [new(bytes, mediaType, "image:1")]
                    : [];
            }
            if (ext == ".pdf") return PdfVisuals(filePath, maxAssets, maxBytesPerAsset);
            if (ext == ".docx") return DocxVisuals(filePath, maxAssets, maxBytesPerAsset);
            if (ext == ".xlsx") return XlsxVisuals(filePath, maxAssets, maxBytesPerAsset);
            if (ext == ".pptx") return PptxVisuals(filePath, maxAssets, maxBytesPerAsset);
        }
        catch
        {
            // Text extraction remains independently useful. The processing service decides
            // whether an absent/failed visual enrichment should fail the durable job.
        }
        return [];
    }

    private static List<AttachmentTextSegment> ExtractFromPdf(string filePath, int readLimit)
    {
        using var document = PdfDocument.Open(filePath);
        var result = new List<AttachmentTextSegment>();
        var read = 0;
        foreach (var page in document.GetPages())
        {
            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                result.Add(new(page.Text.Trim(), $"page:{page.Number}"));
                read += page.Text.Length;
            }
            if (read >= readLimit) break;
        }
        return result;
    }

    private static List<AttachmentTextSegment> ExtractFromDocx(string filePath, int readLimit)
    {
        using var stream = File.OpenRead(filePath);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return [];

        var segments = new List<AttachmentTextSegment>();
        var text = new StringBuilder();
        var section = 0;
        var table = 0;
        var read = 0;
        void FlushText()
        {
            if (text.Length == 0) return;
            segments.Add(new(text.ToString().Trim(), $"document:section:{++section}"));
            read += text.Length;
            text.Clear();
        }

        foreach (var child in body.ChildElements)
        {
            if (child is W.Paragraph paragraph)
            {
                var value = paragraph.InnerText.Trim();
                if (value.Length == 0) continue;
                var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                var heading = style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase);
                if (heading) text.Append("## ");
                text.AppendLine(value).AppendLine();
            }
            else if (child is W.Table wordTable)
            {
                FlushText();
                var markdown = MarkdownTable(wordTable.Elements<W.TableRow>()
                    .Select(row => row.Elements<W.TableCell>().Select(cell => cell.InnerText).ToList()).ToList());
                if (markdown.Length > 0)
                {
                    segments.Add(new(markdown, $"document:table:{++table}", "table"));
                    read += markdown.Length;
                }
            }
            if (read + text.Length >= readLimit) break;
        }
        FlushText();
        return segments;
    }

    private static List<AttachmentTextSegment> ExtractFromXlsx(string filePath, int readLimit)
    {
        using var doc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart == null) return [];
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var segments = new List<AttachmentTextSegment>();
        var totalRead = 0;

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var rows = new List<List<string>>();
            foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
            {
                var values = new SortedDictionary<int, string>();
                var fallbackColumn = 0;
                foreach (var cell in row.Elements<Cell>())
                {
                    var column = CellColumnIndex(cell.CellReference?.Value) ?? fallbackColumn;
                    values[column] = GetCellText(cell, sharedStrings);
                    fallbackColumn = column + 1;
                }
                if (values.Count == 0) continue;
                var width = values.Keys.Max() + 1;
                rows.Add(Enumerable.Range(0, width).Select(i => values.GetValueOrDefault(i, "")).ToList());
                if (rows.Sum(x => x.Sum(v => v.Length)) + totalRead >= readLimit) break;
            }
            var sheetName = workbookPart.Workbook.Sheets?.Elements<Sheet>()
                .FirstOrDefault(s => s.Id?.Value == workbookPart.GetIdOfPart(worksheetPart))?.Name?.Value
                ?? (segments.Count + 1).ToString();
            var markdown = MarkdownTable(rows);
            if (markdown.Length > 0)
            {
                segments.Add(new($"## Sheet: {sheetName}\n\n{markdown}", $"sheet:{sheetName}", "table"));
                totalRead += markdown.Length;
            }
            if (totalRead >= readLimit) break;
        }
        return segments;
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        string value;
        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings != null
            && int.TryParse(cell.CellValue?.InnerText, out var idx)
            && idx >= 0 && idx < sharedStrings.ChildElements.Count)
            value = sharedStrings.ChildElements[idx].InnerText;
        else if (cell.DataType?.Value == CellValues.InlineString)
            value = cell.InlineString?.InnerText ?? "";
        else
            value = cell.CellValue?.InnerText ?? "";

        var formula = cell.CellFormula?.Text;
        return string.IsNullOrWhiteSpace(formula) ? value : $"={formula} → {value}";
    }

    private static List<AttachmentTextSegment> ExtractFromPptx(string filePath, int readLimit)
    {
        using var doc = PresentationDocument.Open(filePath, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart == null) return [];
        var segments = new List<AttachmentTextSegment>();
        var totalRead = 0;
        foreach (var (slidePart, index) in presentationPart.SlideParts.Select((part, index) => (part, index)))
        {
            var slideNumber = index + 1;
            var tables = slidePart.Slide.Descendants<A.Table>().ToList();
            var tableTexts = tables.Select((table, tableIndex) => new AttachmentTextSegment(
                MarkdownTable(table.Elements<A.TableRow>()
                    .Select(row => row.Elements<A.TableCell>().Select(cell => cell.InnerText).ToList()).ToList()),
                $"slide:{slideNumber}:table:{tableIndex + 1}", "table")).Where(x => x.Text.Length > 0).ToList();

            var tableTextNodes = tables.SelectMany(x => x.Descendants<A.Text>()).ToHashSet();
            var text = string.Join(' ', slidePart.Slide.Descendants<A.Text>()
                .Where(x => !tableTextNodes.Contains(x)).Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)));
            if (text.Length > 0)
                segments.Add(new($"## Slide {slideNumber}\n\n{text}", $"slide:{slideNumber}"));
            segments.AddRange(tableTexts);
            totalRead += text.Length + tableTexts.Sum(x => x.Text.Length);
            if (totalRead >= readLimit) break;
        }
        return segments;
    }

    private static List<AttachmentTextSegment> ExtractFromDelimited(string filePath, int readLimit)
    {
        var rows = new List<List<string>>();
        var read = 0;
        using var parser = new TextFieldParser(filePath, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        while (!parser.EndOfData && read < readLimit)
        {
            var fields = parser.ReadFields() ?? [];
            rows.Add(fields.ToList());
            read += fields.Sum(x => x.Length);
        }
        var table = MarkdownTable(rows);
        return table.Length == 0 ? [] : [new(table, "file:table:1", "table")];
    }

    private static List<AttachmentTextSegment> ExtractFromSvg(string filePath, int readLimit)
    {
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit };
        using var reader = System.Xml.XmlReader.Create(filePath, settings);
        var doc = XDocument.Load(reader);
        var values = doc.Descendants()
            .Where(x => x.Name.LocalName is "title" or "desc" or "text" or "tspan")
            .Select(x => x.Value.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        var text = string.Join("\n", values);
        if (text.Length > readLimit) text = text[..readLimit];
        return text.Length == 0 ? [] : [new($"## Görsel\n\n{text}", "image:1", "image")];
    }

    private static List<AttachmentTextSegment> ExtractFromTextFile(string filePath, int readLimit)
    {
        using var reader = new StreamReader(filePath);
        var buffer = new char[readLimit];
        var read = reader.Read(buffer, 0, readLimit);
        var text = new string(buffer, 0, read);
        return string.IsNullOrWhiteSpace(text) ? [] : [new(text, "file")];
    }

    internal static string MarkdownTable(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count == 0) return "";
        var width = rows.Max(x => x.Count);
        if (width == 0) return "";
        var normalized = rows.Select(row => Enumerable.Range(0, width)
            .Select(i => EscapeCell(i < row.Count ? row[i] : "")).ToList()).ToList();
        var header = normalized[0];
        if (header.All(string.IsNullOrWhiteSpace))
            header = Enumerable.Range(1, width).Select(i => $"Kolon {i}").ToList();
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", header)).AppendLine(" |");
        sb.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", width))).AppendLine(" |");
        foreach (var row in normalized.Skip(1))
            sb.Append("| ").Append(string.Join(" | ", row)).AppendLine(" |");
        return sb.ToString().TrimEnd();
    }

    private static string EscapeCell(string value) => value.Replace("\r", " ").Replace("\n", "<br>")
        .Replace("|", "\\|").Trim();

    internal static string TruncatePreservingStructure(string text, int limit, string kind)
    {
        if (text.Length <= limit) return text;
        if (kind is "table" or "mixed-table")
        {
            var kept = new List<string>();
            var length = 0;
            foreach (var line in text.Replace("\r", "").Split('\n'))
            {
                var added = line.Length + (kept.Count == 0 ? 0 : 1);
                if (length + added > limit) break;
                kept.Add(line);
                length += added;
            }
            if (kept.Count >= 2) return string.Join('\n', kept);
        }
        var cut = text[..limit];
        var boundary = cut.LastIndexOfAny(['\n', ' ', '\t']);
        return boundary >= limit / 2 ? cut[..boundary].TrimEnd() : cut;
    }

    private static int? CellColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var result = 0;
        var found = false;
        foreach (var c in reference)
        {
            if (!char.IsLetter(c)) break;
            found = true;
            result = checked(result * 26 + char.ToUpperInvariant(c) - 'A' + 1);
        }
        return found ? result - 1 : null;
    }

    private static IReadOnlyList<AttachmentVisualAsset> PdfVisuals(string path, int max, int maxBytes)
    {
        using var pdf = PdfDocument.Open(path);
        var result = new List<AttachmentVisualAsset>();
        foreach (var page in pdf.GetPages())
        foreach (var (image, index) in page.GetImages().Select((image, index) => (image, index)))
        {
            if (image.WidthInSamples < 128 || image.HeightInSamples < 128) continue;
            if (!image.TryGetPng(out var png) || png.Length > maxBytes) continue;
            result.Add(new(png, "image/png", $"page:{page.Number}:image:{index + 1}"));
            if (result.Count >= max) return result;
        }
        return result;
    }

    private static IReadOnlyList<AttachmentVisualAsset> DocxVisuals(string path, int max, int maxBytes)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var parts = doc.MainDocumentPart?.ImageParts ?? [];
        return PackageVisuals(parts, "document:image", max, maxBytes);
    }

    private static IReadOnlyList<AttachmentVisualAsset> PptxVisuals(string path, int max, int maxBytes)
    {
        using var doc = PresentationDocument.Open(path, false);
        var result = new List<AttachmentVisualAsset>();
        if (doc.PresentationPart == null) return result;
        foreach (var (slide, slideIndex) in doc.PresentationPart.SlideParts.Select((x, i) => (x, i)))
        {
            var visuals = PackageVisuals(slide.ImageParts, $"slide:{slideIndex + 1}:image",
                max - result.Count, maxBytes);
            result.AddRange(visuals);
            if (result.Count >= max) break;
        }
        return result;
    }

    private static IReadOnlyList<AttachmentVisualAsset> XlsxVisuals(string path, int max, int maxBytes)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        var result = new List<AttachmentVisualAsset>();
        var workbookPart = doc.WorkbookPart;
        if (workbookPart == null) return result;
        foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            if (sheet.Id?.Value == null || workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheet)
                continue;
            var drawing = worksheet.DrawingsPart;
            if (drawing == null) continue;
            result.AddRange(PackageVisuals(drawing.ImageParts, $"sheet:{sheet.Name?.Value ?? "?"}:image",
                max - result.Count, maxBytes));
            if (result.Count >= max) break;
        }
        return result;
    }

    private static IReadOnlyList<AttachmentVisualAsset> PackageVisuals(IEnumerable<ImagePart> parts,
        string location, int max, int maxBytes)
    {
        var result = new List<AttachmentVisualAsset>();
        foreach (var (part, index) in parts.Select((x, i) => (x, i)))
        {
            if (part.ContentType is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp")) continue;
            using var source = part.GetStream(FileMode.Open, FileAccess.Read);
            if (source.Length > maxBytes) continue;
            using var memory = new MemoryStream();
            source.CopyTo(memory);
            result.Add(new(memory.ToArray(), part.ContentType, $"{location}:{index + 1}"));
            if (result.Count >= max) break;
        }
        return result;
    }

    private static (List<AttachmentTextSegment> Segments, bool Truncated) LimitSegments(
        IEnumerable<AttachmentTextSegment> segments, int maxCharacters)
    {
        var result = new List<AttachmentTextSegment>();
        var remaining = maxCharacters;
        var truncated = false;
        foreach (var segment in segments)
        {
            if (remaining <= 0) { truncated = true; break; }
            var text = segment.Text.Trim();
            if (text.Length == 0) continue;
            if (text.Length > remaining)
            {
                text = TruncatePreservingStructure(text, remaining, segment.Kind);
                truncated = true;
            }
            result.Add(segment with { Text = text });
            remaining -= text.Length;
        }
        return (result, truncated);
    }
}
