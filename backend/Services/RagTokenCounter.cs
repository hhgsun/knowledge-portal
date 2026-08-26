using System.Text;

namespace KnowledgePortal.Api.Services;

public interface IRagTokenCounter
{
    int CountTokens(string text);
    string TruncateToTokens(string text, int maxTokens, out int tokenCount, out bool truncated);
    void ObserveActualCount(int estimatedTokens, int actualTokens);
    string Strategy { get; }
}

/// <summary>
/// Preflight token budgeter for the local Qwen model. Ollama reports exact prompt token usage only
/// after completion, so this counter starts with a conservative Unicode-aware estimate and
/// continuously calibrates that estimate from ChatResponse.Usage.InputTokenCount. It never expands a
/// budget after calibration beyond the configured safety clamps.
/// </summary>
public sealed class RagTokenCounter(IConfiguration? config = null) : IRagTokenCounter
{
    private readonly double _latinCharactersPerToken = Math.Clamp(
        config?.GetValue("Ollama:RagTokenizer:LatinCharactersPerToken", 3.2) ?? 3.2, 1.5, 6);
    private double _calibration = 1;

    public string Strategy => "qwen_unicode_estimator_calibrated_v1";

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var baseEstimate = EstimateBase(text);
        var factor = Volatile.Read(ref _calibration);
        return Math.Max(1, (int)Math.Ceiling(baseEstimate * factor));
    }

    public string TruncateToTokens(string text, int maxTokens, out int tokenCount, out bool truncated)
    {
        if (maxTokens <= 0 || string.IsNullOrEmpty(text))
        {
            tokenCount = 0;
            truncated = text.Length > 0;
            return "";
        }

        var fullCount = CountTokens(text);
        if (fullCount <= maxTokens)
        {
            tokenCount = fullCount;
            truncated = false;
            return text;
        }

        var boundaries = new List<int> { 0 };
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            offset += rune.Utf16SequenceLength;
            boundaries.Add(offset);
        }

        var low = 0;
        var high = boundaries.Count - 1;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (CountTokens(text[..boundaries[middle]]) <= maxTokens) low = middle;
            else high = middle - 1;
        }

        var prefix = text[..boundaries[low]].TrimEnd();
        tokenCount = CountTokens(prefix);
        truncated = true;
        return prefix;
    }

    public void ObserveActualCount(int estimatedTokens, int actualTokens)
    {
        if (estimatedTokens <= 0 || actualTokens <= 0) return;
        var observedRatio = Math.Clamp(actualTokens / (double)estimatedTokens, .75, 2);
        while (true)
        {
            var current = Volatile.Read(ref _calibration);
            // Bias upward quickly for safety; relax downward slowly to avoid oscillating budgets.
            var weight = observedRatio > 1 ? .35 : .1;
            // The observation is actual/current-estimate, so it must adjust the current factor
            // multiplicatively. Averaging it directly with the factor would drift back toward 1
            // even when the calibrated estimate exactly matches actual model usage.
            var updated = Math.Clamp(current * ((1 - weight) + observedRatio * weight), .75, 2);
            if (Interlocked.CompareExchange(ref _calibration, updated, current) == current) return;
        }
    }

    private int EstimateBase(string text)
    {
        double tokens = 0;
        var latinRun = 0;
        void FlushLatin()
        {
            if (latinRun == 0) return;
            tokens += Math.Ceiling(latinRun / _latinCharactersPerToken);
            latinRun = 0;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) { FlushLatin(); continue; }
            if (rune.IsAscii && Rune.IsLetterOrDigit(rune)) { latinRun++; continue; }
            FlushLatin();
            // Non-ASCII letters (including Turkish) and punctuation are conservatively one token.
            tokens += 1;
        }
        FlushLatin();
        return Math.Max(1, (int)Math.Ceiling(tokens));
    }
}
