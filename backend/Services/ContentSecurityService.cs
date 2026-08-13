using System.Text.RegularExpressions;

namespace KnowledgePortal.Api.Services;

public record ContentSecurityAssessment(
    string RiskLevel,
    string[] Signals,
    int SecretsRedacted,
    bool TreatAsUntrustedData = true,
    bool AllowAutomaticExecution = false);

/// <summary>Conservative, explainable first-line scanning. It flags rather than blocks content.</summary>
public static partial class ContentSecurityService
{
    private static readonly (string Name, Regex Pattern)[] InjectionPatterns =
    [
        ("instruction_override", OverrideRegex()),
        ("system_prompt_extraction", PromptExtractionRegex()),
        ("credential_exfiltration", ExfiltrationRegex()),
        ("tool_or_command_execution", ExecutionRegex()),
        ("role_impersonation", RoleRegex())
    ];

    private static readonly Regex[] SecretPatterns =
    [
        PortalKeyRegex(), BearerRegex(), JwtRegex(), AwsKeyRegex(), AssignedSecretRegex()
    ];

    public static ContentSecurityAssessment Assess(string? text)
    {
        text ??= "";
        var signals = InjectionPatterns.Where(p => p.Pattern.IsMatch(text)).Select(p => p.Name).Distinct().ToArray();
        var secretCount = SecretPatterns.Sum(pattern => pattern.Matches(text).Count);
        var risk = signals.Length > 0 && secretCount > 0 ? "critical"
            : signals.Length > 0 ? "high"
            : secretCount > 0 ? "medium"
            : "low";
        return new ContentSecurityAssessment(risk, signals, secretCount);
    }

    public static string? RedactSecrets(string? text)
    {
        if (text == null) return null;
        foreach (var pattern in SecretPatterns)
            text = pattern.Replace(text, "[REDACTED_SECRET]");
        return text;
    }

    [GeneratedRegex(@"(?i)\b(ignore|disregard|forget|override)\b.{0,80}\b(previous|prior|system|developer|instructions?|rules?)\b|\b(önceki|sistem|geliştirici)\b.{0,80}\b(talimat|kurallar?).{0,30}\b(yok say|unut|geçersiz)\b")]
    private static partial Regex OverrideRegex();
    [GeneratedRegex(@"(?i)\b(show|reveal|print|return|göster|açıkla)\b.{0,60}\b(system prompt|developer message|gizli talimat|sistem mesajı)\b")]
    private static partial Regex PromptExtractionRegex();
    [GeneratedRegex(@"(?i)\b(send|upload|post|exfiltrate|gönder|yükle)\b.{0,80}\b(token|secret|credential|api key|environment|ortam değişken)\b")]
    private static partial Regex ExfiltrationRegex();
    [GeneratedRegex(@"(?i)\b(run|execute|invoke|çalıştır|yürüt)\b.{0,60}\b(shell|command|powershell|bash|tool|curl|wget|komut|araç)\b")]
    private static partial Regex ExecutionRegex();
    [GeneratedRegex(@"(?i)\b(you are now|act as|pretend to be|bundan sonra sen|rolünü değiştir)\b")]
    private static partial Regex RoleRegex();
    [GeneratedRegex(@"\bkp_[A-Za-z0-9_-]{12,}\b")]
    private static partial Regex PortalKeyRegex();
    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{12,}")]
    private static partial Regex BearerRegex();
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b")]
    private static partial Regex JwtRegex();
    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsKeyRegex();
    [GeneratedRegex(@"(?i)\b(api[_ -]?key|secret|password|token)\b\s*[:=]\s*['""]?[A-Za-z0-9._~+/=-]{12,}['""]?")]
    private static partial Regex AssignedSecretRegex();
}
