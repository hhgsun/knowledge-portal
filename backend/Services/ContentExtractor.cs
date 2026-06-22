using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowledgePortal.Api.Services;

public static class ContentExtractor
{
    public static string ExtractSearchableText(string title, string? excerpt, string? contentJson)
    {
        var sb = new StringBuilder();
        sb.Append(title);

        if (!string.IsNullOrWhiteSpace(excerpt))
        {
            sb.Append(". ");
            sb.Append(excerpt);
        }

        if (!string.IsNullOrWhiteSpace(contentJson))
        {
            try
            {
                var text = ExtractTextFromJson(JsonDocument.Parse(contentJson).RootElement);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.Append(". ");
                    sb.Append(text.Trim());
                }
            }
            catch
            {
                // Malformed JSON — skip content extraction
            }
        }

        return sb.ToString();
    }

    public static string ExtractTextFromJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? "";
            case JsonValueKind.Object:
                var sb = new StringBuilder();
                if (element.TryGetProperty("text", out var textProp))
                    sb.Append(textProp.GetString() ?? "").Append(' ');
                if (element.TryGetProperty("content", out var contentProp))
                    sb.Append(ExtractTextFromJson(contentProp));
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name != "text" && prop.Name != "content")
                        sb.Append(ExtractTextFromJson(prop.Value));
                }
                return sb.ToString();
            case JsonValueKind.Array:
                var arrSb = new StringBuilder();
                foreach (var item in element.EnumerateArray())
                    arrSb.Append(ExtractTextFromJson(item)).Append(' ');
                return arrSb.ToString();
            default:
                return "";
        }
    }

    public static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }
}
