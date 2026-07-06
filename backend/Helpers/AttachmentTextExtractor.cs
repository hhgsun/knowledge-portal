using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

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

    private static string ExtractFromTextFile(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var buffer = new char[MaxCharacters];
        var read = reader.Read(buffer, 0, MaxCharacters);
        return new string(buffer, 0, read);
    }
}
