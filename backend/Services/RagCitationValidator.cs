using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

public record RagClaim(string Text, List<string> SourceIds, string Role = "explanation");
public record RagEvidence(string SourceId, string ArticleId, string Title, string Slug, string SourceType,
    string? AttachmentId, string? SourceName, string? SourceLocation, string Passage, double Score,
    string? ChunkId = null, string? CanonicalUrl = null, int? PageNumber = null,
    int AuthorityWeight = 50, bool Approved = false, string ReviewState = "not_recorded",
    int ReliabilityScore = 50, string? UpdatedAt = null);
public record ValidatedRagAnswer(string Answer, List<RagClaim> Claims, bool InsufficientContext,
    double CitationCoverage, double ClaimSupportCoverage, string GroundingStatus, List<string> Warnings);

public static partial class RagCitationValidator
{
    private const string RefuseInsufficient = "Bu konuda yeterli bilgi bulamadım.";
    private const double MinimumLexicalSupport = .65;
    private sealed record ModelOutput(string? Answer, List<RagClaim>? Claims, bool? InsufficientContext,
        bool RecoveredFromCitedText = false);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static ValidatedRagAnswer Validate(string raw, IReadOnlyCollection<RagEvidence> evidence,
        string? question = null)
    {
        var warnings = new List<string>();
        var parsed = TryParse(raw);
        if (parsed == null)
            return new(RefuseInsufficient, [], true, 0, 0, "rejected_unstructured",
                ["Model output was rejected because it did not match the required structured JSON contract."]);

        if (parsed.RecoveredFromCitedText)
            warnings.Add("Model returned cited text instead of JSON; evidence-bound claims were recovered and validated.");

        if (parsed.InsufficientContext == true)
            return new(RefuseInsufficient, [], true, 1, 1, "insufficient_context", warnings);

        var allowed = evidence.Select(x => x.SourceId).ToHashSet(StringComparer.Ordinal);
        var evidenceById = evidence.ToDictionary(x => x.SourceId, StringComparer.Ordinal);
        // Models occasionally put a document title, an excerpt, and the actual answer into one
        // claim. Validate each sentence independently so supported answer text cannot smuggle an
        // irrelevant catalogue-style sentence through the grounding check.
        var candidateClaims = (parsed.Claims ?? [])
            .Where(claim => !string.IsNullOrWhiteSpace(claim.Text))
            .SelectMany(claim => EvidenceSentences(claim.Text)
                .Select(sentence => new RagClaim(sentence, claim.SourceIds ?? [], NormalizeRole(claim.Role))))
            .ToList();
        var twoPartTopicQuery = IsBareTopicQuery(question) || IsConfigurationDefinitionQuery(question);
        var requiresTopicExplanation = twoPartTopicQuery && HasMultipleRelevantEvidenceSentences(question!, evidence);
        var configurationDefinitionSourceIds = new HashSet<string>(StringComparer.Ordinal);
        var topicAlignedSourceIds = new HashSet<string>(StringComparer.Ordinal);
        var claims = new List<RagClaim>();
        var valid = 0;
        var supported = 0;
        var claimCount = 0;
        foreach (var claim in candidateClaims)
        {
            var requestedIds = claim.SourceIds ?? [];
            var ids = requestedIds.Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count != requestedIds.Count)
                warnings.Add($"Claim contained unknown evidence IDs: {claim.Text[..Math.Min(80, claim.Text.Length)]}");

            // A repeated document title is retrieval metadata, not an attempted factual answer.
            // Drop it before coverage accounting. Other catalogue prose (for example an excerpt
            // describing what a guide covers) is still counted and must pass question alignment.
            if (ids.Any(id => IsTitleOnlyClaim(claim.Text, evidenceById[id])))
            {
                warnings.Add($"Document title was removed from the answer: {claim.Text[..Math.Min(80, claim.Text.Length)]}");
                continue;
            }

            claimCount++;
            if (ids.Count > 0) valid++;
            if (ids.Count == 0) continue;

            var directlyResponsive = IsResponsiveToQuestion(question, claim.Text);
            var topicallyAligned = ids.Any(topicAlignedSourceIds.Contains) ||
                IsTopicallyAligned(question, claim.Text, ids.Select(id => evidenceById[id]));
            var responsiveConfigurationExplanation = IsConfigurationDefinitionQuery(question) &&
                configurationDefinitionSourceIds.Count > 0 &&
                IsRelevantConfigurationExplanation(question!, claim.Text, ids, configurationDefinitionSourceIds);
            if ((!directlyResponsive || !topicallyAligned) && !responsiveConfigurationExplanation)
            {
                warnings.Add(!directlyResponsive
                    ? $"Claim was supported by source metadata but did not directly answer the definition question: {claim.Text[..Math.Min(80, claim.Text.Length)]}"
                    : $"Claim was grounded but did not match the requested topic: {claim.Text[..Math.Min(80, claim.Text.Length)]}");
                continue;
            }

            // Validate against local sentence/clause windows from each cited item independently.
            // Joining whole chunks lets an unrelated negative sentence in one source flip the
            // polarity of an otherwise supported positive claim (and vice versa).
            var supportingIds = ids
                .Where(id => IsSupportedByEvidence(claim.Text, evidenceById[id].Passage))
                .ToList();
            if (supportingIds.Count == 0)
            {
                warnings.Add($"Unsupported claim was removed: {claim.Text[..Math.Min(80, claim.Text.Length)]}");
                continue;
            }
            if (supportingIds.Count != ids.Count)
                warnings.Add($"Non-supporting evidence IDs were removed from claim: {claim.Text[..Math.Min(80, claim.Text.Length)]}");

            supported++;
            claims.Add(new RagClaim(claim.Text.Trim(), supportingIds, NormalizeRole(claim.Role)));
            if (topicallyAligned) topicAlignedSourceIds.UnionWith(supportingIds);
            if (directlyResponsive && IsConfigurationDefinitionQuery(question))
                configurationDefinitionSourceIds.UnionWith(supportingIds);
        }

        var coverage = claimCount == 0 ? 0 : valid / (double)claimCount;
        var supportCoverage = claimCount == 0 ? 0 : supported / (double)claimCount;

        foreach (var id in CitationRegex().Matches(parsed.Answer ?? "").Select(m => m.Groups[1].Value).Distinct())
            if (!allowed.Contains(id)) warnings.Add($"Unknown citation {id} was rejected.");

        if (claims.Count == 0)
            return new(RefuseInsufficient, [], true, coverage, supportCoverage, "rejected_unsupported", warnings.Distinct().ToList());

        // Punctuation is deliberately irrelevant here. A local model may turn
        // "Reranking:External: ..." into "Reranking:External, ..." and otherwise bypass a
        // catalogue-echo check. A bare topic answer therefore needs at least two independently
        // supported claims: the first is the retained summary, the rest form the explanation.
        if (requiresTopicExplanation && claims.Count < 2)
        {
            warnings.Add("A bare topic answer with multiple relevant evidence facts requires a supported summary and at least one separate supported explanatory claim.");
            // Preserve the independently grounded summary internally. The RAG service first asks
            // the model to repair the incomplete answer; if the local model still returns only the
            // summary, a verified evidence sentence can be appended as a transparent enrichment.
            return new(RefuseInsufficient, claims, true, coverage, supportCoverage,
                "rejected_unsupported", warnings.Distinct().ToList());
        }

        claims = NormalizeClaimOrder(claims);

        // The model's prose is not trusted independently from its structured claims. Rebuild the
        // user-visible answer exclusively from claims that survived evidence-id and support checks,
        // so uncited sentences cannot bypass validation through the free-form answer field.
        static string RenderClaim(RagClaim claim) =>
            $"{claim.Text.Trim()} {string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))}";
        var answer = twoPartTopicQuery && claims.Count > 1
            ? $"{RenderClaim(claims[0])}\n\n" + string.Join('\n', claims.Skip(1).Select(RenderClaim))
            : string.Join('\n', claims.Select(RenderClaim));

        var status = coverage == 1 && supportCoverage == 1 ? "lexically_grounded" : "partially_grounded";
        return new(answer, claims, false, coverage, supportCoverage, status, warnings.Distinct().ToList());
    }

    /// <summary>
    /// Builds the only user-visible prose from claims that already passed grounding. The first claim
    /// is the fast answer; additional claims are rendered as an explanation so clients do not have to
    /// turn a flat evidence list into a readable synthesis themselves.
    /// </summary>
    public static string RenderSupportedAnswer(IReadOnlyCollection<RagClaim> claims, string? question,
        string fallbackAnswer, bool insufficientContext)
    {
        if (insufficientContext || claims.Count == 0) return fallbackAnswer;

        static string Render(RagClaim claim) =>
            $"{claim.Text.Trim()} {string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))}";

        var ordered = NormalizeClaimOrder(claims.ToList());
        if (ordered.Count == 1) return Render(ordered[0]);

        var english = LooksEnglish(question);
        var sections = new[]
        {
            (Role: "explanation", Tr: "Açıklama", En: "Explanation", Ordered: false),
            (Role: "step", Tr: "Adımlar", En: "Steps", Ordered: true),
            (Role: "constraint", Tr: "Sınırlar", En: "Constraints", Ordered: false),
            (Role: "exception", Tr: "İstisnalar", En: "Exceptions", Ordered: false),
            (Role: "conflict", Tr: "Kaynak uyuşmazlıkları", En: "Source conflicts", Ordered: false)
        };
        var blocks = new List<string> { Render(ordered[0]) };
        foreach (var section in sections)
        {
            var sectionClaims = ordered.Skip(1).Where(claim => claim.Role == section.Role).ToList();
            if (sectionClaims.Count == 0) continue;
            var lines = sectionClaims.Select((claim, index) =>
                $"{(section.Ordered ? $"{index + 1}." : "-")} {Render(claim)}");
            blocks.Add($"**{(english ? section.En : section.Tr)}**\n\n{string.Join('\n', lines)}");
        }
        return string.Join("\n\n", blocks);
    }

    public static List<RagClaim> NormalizeRoles(IReadOnlyCollection<RagClaim> claims) =>
        NormalizeClaimOrder(claims);

    /// <summary>
    /// Estimates how many distinct answer facts the supplied evidence can support. The estimate is
    /// intentionally conservative: it counts only distinct, safe declarative sentences from the
    /// already query-ranked and ACL-filtered evidence. This lets the comprehensive profile target
    /// useful breadth without using source-block count as a proxy for information density.
    /// </summary>
    public static int EstimateRelevantFactCapacity(string question,
        IReadOnlyCollection<RagEvidence> evidence, int maximum)
    {
        maximum = Math.Max(1, maximum);
        if (evidence.Count == 0) return 0;

        var candidates = evidence.SelectMany(item => EvidenceSentences(item.Passage)
                .Select(sentence => MarkdownPrefixRegex().Replace(sentence.Trim(), "").Trim())
                .Where(sentence => sentence.Length is >= 8 and <= 700)
                .Where(sentence => !SameNormalizedText(sentence, item.Title))
                .Where(IsDeclarativeExplanation)
                .Where(sentence => IsTopicallyAligned(question, sentence, [item]))
                .Where(sentence => ContentSecurityService.Assess(sentence).RiskLevel is not ("high" or "critical")))
            .Select(NormalizeComparableText)
            .Where(sentence => sentence.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(maximum)
            .Count();
        return Math.Max(1, candidates);
    }

    public static ValidatedRagAnswer? TryBuildExtractiveFallback(string question,
        IReadOnlyCollection<RagEvidence> evidence, int maxClaims = 4, string? reason = null)
    {
        var queryTokens = SignificantTokens(question);
        if (queryTokens.Count == 0) return null;

        var candidates = evidence.SelectMany(item =>
                EvidenceSentences(item.Passage)
                    .Select(sentence => MarkdownPrefixRegex().Replace(sentence.Trim(), "").Trim())
                    .Where(sentence => sentence.Length is >= 20 and <= 500)
                    .Where(sentence => !SameNormalizedText(sentence, item.Title))
                    .Where(sentence => IsResponsiveToQuestion(question, sentence))
                    .Where(sentence => IsTopicallyAligned(question, sentence, [item]))
                    .Where(sentence => ContentSecurityService.Assess(sentence).RiskLevel is not ("high" or "critical"))
                    .Select(sentence => new
                    {
                        item.SourceId,
                        Text = sentence,
                        Overlap = SignificantTokens(sentence).Count(queryTokens.Contains),
                        item.Score
                    }))
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .ThenByDescending(x => x.Score)
            .GroupBy(x => SlugHelper.Transliterate(x.Text).ToLowerInvariant(), StringComparer.Ordinal)
            .Select(x => x.First())
            .Take(Math.Clamp(maxClaims, 1, 8))
            .Select(x => new RagClaim(x.Text, [x.SourceId]))
            .ToList();

        if (candidates.Count == 0) return null;

        // These are verbatim, secret-redacted sentences selected from known evidence IDs, rather
        // than model-authored paraphrases. Numeric and negation consistency therefore hold by
        // construction, while the normal model path remains subject to the stricter validator.
        var answer = string.Join("\n", candidates.Select(claim =>
            $"{claim.Text} {string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))}"));
        return new ValidatedRagAnswer(answer, candidates, false, 1, 1, "extractive_fallback",
            [$"{reason ?? "Structured model output failed"}; returning query-relevant passages extracted from verified evidence."]);
    }

    public static ValidatedRagAnswer? TryEnrichSupportedSummary(string question,
        IReadOnlyCollection<RagEvidence> evidence, IReadOnlyCollection<RagClaim> supportedSummary,
        int maxExplanationClaims = 3, string? reason = null)
    {
        if (supportedSummary.Count == 0) return null;
        var topic = ConfigurationSubject(question) ?? DefinitionSubject(question) ?? question;
        var queryTokens = SignificantTokens(topic);
        if (queryTokens.Count == 0) return null;
        var summaryTexts = supportedSummary.Select(x => NormalizeComparableText(x.Text))
            .ToHashSet(StringComparer.Ordinal);
        var summarySourceIds = supportedSummary.SelectMany(x => x.SourceIds).ToHashSet(StringComparer.Ordinal);

        var explanations = evidence.SelectMany(item =>
                EvidenceSentences(item.Passage)
                    .Select(sentence => MarkdownPrefixRegex().Replace(sentence.Trim(), "").Trim())
                    .Where(sentence => sentence.Length is >= 20 and <= 500)
                    .Where(sentence => !SameNormalizedText(sentence, item.Title))
                    .Where(sentence => !summaryTexts.Contains(NormalizeComparableText(sentence)))
                    .Where(IsDeclarativeExplanation)
                    .Where(sentence => IsTopicallyAligned(question, sentence, [item]))
                    .Where(sentence => SignificantTokens(sentence).Any(queryTokens.Contains) ||
                                       summarySourceIds.Contains(item.SourceId))
                    .Where(sentence => !supportedSummary.Any(summary =>
                        IsSupportedByEvidence(summary.Text, sentence)))
                    .Where(sentence => ContentSecurityService.Assess(sentence).RiskLevel is not ("high" or "critical"))
                    .Select(sentence => new
                    {
                        item.SourceId,
                        Text = sentence,
                        Overlap = SignificantTokens(sentence).Count(queryTokens.Contains),
                        item.Score
                    }))
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .ThenByDescending(x => x.Score)
            .GroupBy(x => NormalizeComparableText(x.Text), StringComparer.Ordinal)
            .Select(x => x.First())
            .Take(Math.Clamp(maxExplanationClaims, 1, 6))
            .Select(x => new RagClaim(x.Text, [x.SourceId]))
            .ToList();
        if (explanations.Count == 0) return null;

        static string Render(RagClaim claim) =>
            $"{claim.Text.Trim()} {string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))}";
        var summaries = supportedSummary.ToList();
        var answer = $"{string.Join('\n', summaries.Select(Render))}\n\n" +
                     string.Join('\n', explanations.Select(Render));
        return new ValidatedRagAnswer(answer, summaries.Concat(explanations).ToList(), false,
            1, 1, "extractive_enrichment",
            [$"{reason ?? "The model returned only a supported summary"}; verified source passages were appended as an explanatory paragraph."]);
    }

    public static ValidatedRagAnswer? TryBuildConfigurationExplanationFallback(string question,
        IReadOnlyCollection<RagEvidence> evidence, int maxExplanationClaims = 3, string? reason = null)
    {
        var subject = ConfigurationSubject(question);
        if (subject == null) return null;
        var subjectTokens = SignificantTokens(subject);
        if (subjectTokens.Count == 0) return null;

        var summary = evidence
            .Select(item => new { Evidence = item, Text = ExtractConfigurationEntry(item.Passage, subject) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderByDescending(x => x.Evidence.Score)
            .Select(x => new RagClaim(x.Text!, [x.Evidence.SourceId]))
            .FirstOrDefault();
        var summaryText = summary == null ? null : NormalizeComparableText(summary.Text);

        var explanations = evidence.SelectMany(item =>
                EvidenceSentences(item.Passage)
                    .Select(sentence => MarkdownPrefixRegex().Replace(sentence.Trim(), "").Trim())
                    .Where(sentence => sentence.Length is >= 20 and <= 500)
                    .Where(sentence => !SameNormalizedText(sentence, item.Title))
                    .Where(IsDeclarativeExplanation)
                    // A flattened documentation sentence can contain the extracted configuration
                    // entry plus neighbouring keys. It is not a separate explanation and must not
                    // be repeated below the concise summary.
                    .Where(sentence => summaryText == null ||
                                       !NormalizeComparableText(sentence).Contains(summaryText, StringComparison.Ordinal))
                    .Where(sentence => ContentSecurityService.Assess(sentence).RiskLevel is not ("high" or "critical"))
                    .Select(sentence => new
                    {
                        item.SourceId,
                        Text = sentence,
                        Overlap = SignificantTokens(sentence).Count(subjectTokens.Contains),
                        item.Score
                    }))
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .ThenByDescending(x => x.Score)
            .GroupBy(x => NormalizeComparableText(x.Text), StringComparer.Ordinal)
            .Select(x => x.First())
            .Take(Math.Clamp(maxExplanationClaims, 1, 6))
            .Select(x => new RagClaim(x.Text, [x.SourceId]))
            .ToList();
        if (summary == null && explanations.Count == 0) return null;

        static string Render(RagClaim claim) =>
            $"{claim.Text.Trim()} {string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))}";
        var claims = summary == null ? explanations : [summary, .. explanations];
        var answer = summary != null && explanations.Count > 0
            ? $"{Render(summary)}\n\n{string.Join('\n', explanations.Select(Render))}"
            : string.Join('\n', claims.Select(Render));
        return new ValidatedRagAnswer(answer, claims, false, 1, 1, "extractive_fallback",
            [$"{reason ?? "The model did not produce supported configuration claims"}; returning a query-focused configuration entry and verified explanatory passages."]);
    }

    private static ModelOutput? TryParse(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```")) text = CodeFenceRegex().Replace(text, "").Trim();
        if (TryDeserialize(text, out var parsed)) return parsed;

        // Some local models prepend a short explanation or a thinking marker even when JSON mode
        // is requested. Accept only a complete JSON object from such a wrapper; the answer is still
        // rebuilt exclusively from evidence-bound claims below, so free text around it is ignored.
        ModelOutput? extracted = null;
        var containedJsonObject = false;
        foreach (var candidate in ExtractJsonObjects(text))
        {
            containedJsonObject = true;
            if (TryDeserialize(candidate, out parsed)) extracted = parsed;
        }
        if (extracted != null) return extracted;

        // A JSON-looking response with a broken contract must not be reinterpreted as prose merely
        // because a citation happens to occur inside one of its fields.
        return containedJsonObject ? null : TryParseCitedText(text);
    }

    private static bool TryDeserialize(string text, out ModelOutput? parsed)
    {
        try
        {
            parsed = JsonSerializer.Deserialize<ModelOutput>(text, Json);
            // `answer` is a legacy compatibility field. New generation contracts return claims
            // only; user-visible prose is always rebuilt from independently grounded claims.
            return parsed is { Claims: not null, InsufficientContext: not null }
                && parsed.Claims.All(x => x is { Text: not null, SourceIds: not null });
        }
        catch (JsonException)
        {
            parsed = null;
            return false;
        }
    }

    private static IEnumerable<string> ExtractJsonObjects(string text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            if (text[start] != '{') continue;
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                {
                    yield return text[start..(i + 1)];
                    break;
                }
            }
        }
    }

    private static ModelOutput? TryParseCitedText(string text)
    {
        var visible = ThinkBlockRegex().Replace(text, " ");
        var claims = new List<RagClaim>();
        foreach (var part in visible.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var ids = CitationRegex().Matches(part).Select(x => x.Groups[1].Value)
                .Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) continue;

            var claim = CitationRegex().Replace(part, " ");
            claim = MarkdownPrefixRegex().Replace(claim.Trim(), "").Trim();
            if (!string.IsNullOrWhiteSpace(claim)) claims.Add(new RagClaim(claim, ids));
        }

        // Never recover uncited prose. A recovered claim still has to survive the same known-ID,
        // lexical-overlap, number and negation checks as a schema-produced claim.
        return claims.Count == 0 ? null : new ModelOutput(visible, claims, false, true);
    }

    private static bool IsSupportedByEvidence(string claim, string passage) =>
        SupportWindows(passage).Any(window => IsLexicallySupported(claim, window));

    private static bool IsTitleOnlyClaim(string claim, RagEvidence evidence) =>
        SameNormalizedText(claim, evidence.Title);

    private static bool IsBareTopicQuery(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return false;
        var topic = question.Trim();
        return !topic.EndsWith('?') && topic.Length <= 160 &&
               topic.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 8;
    }

    private static bool IsConfigurationDefinitionQuery(string? question) =>
        DefinitionSubject(question)?.Contains(':', StringComparison.Ordinal) == true;

    private static bool IsRelevantConfigurationExplanation(string question, string claim,
        IReadOnlyCollection<string> claimSourceIds, IReadOnlySet<string> definitionSourceIds)
    {
        if (!IsDeclarativeExplanation(claim)) return false;
        var subject = DefinitionSubject(question);
        if (subject == null) return false;
        var subjectTokens = SignificantTokens(subject);
        var claimTokens = SignificantTokens(claim);

        // A follow-up explanation is relevant when it names part of the requested configuration
        // subject (for example "external cross-encoder"), or continues in the same evidence item
        // as the already validated compact definition. Merely sharing the question word "nedir"
        // must never make an unrelated heading relevant.
        return subjectTokens.Any(claimTokens.Contains) || claimSourceIds.Any(definitionSourceIds.Contains);
    }

    private static bool IsDeclarativeExplanation(string sentence)
    {
        var text = sentence.Trim();
        if (text.Length == 0 || text.EndsWith('?')) return false;
        var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount <= 12 && text.IndexOfAny(['.', '!', ';', ':']) < 0) return false;
        return !TurkishDefinitionQuestionRegex().IsMatch(text) &&
               !EnglishDefinitionQuestionRegex().IsMatch(text);
    }

    private static bool IsTopicallyAligned(string? question, string claim,
        IEnumerable<RagEvidence> citedEvidence)
    {
        if (string.IsNullOrWhiteSpace(question)) return true;
        var topicTokens = TopicTokens(question);
        if (topicTokens.Count == 0) return true;

        var candidateText = claim + " " + string.Join(' ', citedEvidence.Select(item =>
            $"{item.Title} {item.SourceName}"));
        var candidateTokens = SignificantTokens(candidateText);
        var policyTokens = topicTokens.Where(token =>
            token.StartsWith("politika", StringComparison.Ordinal) ||
            token.StartsWith("policy", StringComparison.Ordinal)).ToList();
        if (policyTokens.Count > 0 && !policyTokens.Any(policy => candidateTokens.Any(candidate =>
                InflectionAwareTokenMatch(policy, candidate))))
            return false;
        var matches = topicTokens.Count(topic => candidateTokens.Any(candidate =>
            InflectionAwareTokenMatch(topic, candidate)));
        var required = topicTokens.Count >= 3 ? 2 : 1;
        return matches >= required;
    }

    private static HashSet<string> TopicTokens(string question)
    {
        var intent = new HashSet<string>(StringComparer.Ordinal)
        {
            "acikla", "adim", "adimlar", "all", "anlat", "ayrintili", "butun", "calisir", "does",
            "compare", "comprehensive", "detailed", "detayli", "edilir", "entegre", "everything", "exception", "exceptions",
            "hangi", "hakkinda", "hepsi", "how", "istisna", "istisnalar", "kapsamli", "karsilastir",
            "kullanilir", "kurulur", "listele", "nasil", "neden", "nedir", "nelerdir", "overview", "ozet", "ozetle", "responsibilities",
            "responsibility", "sorumluluk", "sorumluluklar", "summarize", "summary", "temel", "tum", "tumu",
            "uygulanir", "what", "why", "work", "works"
        };
        return SignificantTokens(question).Where(token => !intent.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string? ConfigurationSubject(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return null;
        var subject = DefinitionSubject(question) ?? question.Trim().TrimEnd('?', '!', '.');
        return subject.Contains(':', StringComparison.Ordinal) &&
               !subject.Any(char.IsWhiteSpace) ? subject : null;
    }

    private static string? ExtractConfigurationEntry(string passage, string subject)
    {
        var start = passage.IndexOf(subject, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var segment = passage[start..Math.Min(passage.Length, start + 500)];
        var end = segment.Length;
        var sentenceEnd = segment.IndexOfAny(['.', '!', '?'], subject.Length);
        if (sentenceEnd >= 0) end = Math.Min(end, sentenceEnd + 1);
        var nextKey = NextConfigurationKeyRegex().Match(segment, Math.Min(subject.Length + 1, segment.Length));
        if (nextKey.Success) end = Math.Min(end, nextKey.Index);

        var entry = segment[..end].Trim().TrimEnd(',', ';', ':', '-', '—');
        if (entry.Length <= subject.Length + 3 || entry.Length > 500) return null;
        return entry.EndsWith('.') ? entry : entry + ".";
    }

    private static bool HasMultipleRelevantEvidenceSentences(string question,
        IReadOnlyCollection<RagEvidence> evidence)
    {
        var queryTokens = SignificantTokens(question);
        if (queryTokens.Count == 0) return false;

        return evidence.SelectMany(item => EvidenceSentences(item.Passage)
                .Where(sentence => !SameNormalizedText(sentence, item.Title)))
            .Where(sentence => SignificantTokens(sentence).Any(queryTokens.Contains))
            .Select(NormalizeComparableText)
            .Where(sentence => sentence.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count() == 2;
    }

    /// <summary>
    /// Grounding proves that words are supported by a source; it does not prove that they answer
    /// the user's question. For a pure definition request ("MCP nedir?" / "What is MCP?"), require
    /// a complete definitional proposition about the requested subject. This rejects document
    /// titles and catalogue excerpts while remaining agnostic about the definition itself: if the
    /// source says MCP is a car brand, that source-grounded definition is accepted.
    /// Compound questions keep the normal relevance path because one sentence may answer a
    /// different requested facet without being a definition.
    /// </summary>
    private static bool IsResponsiveToQuestion(string? question, string claim)
    {
        var subject = DefinitionSubject(question);
        if (subject == null) return true;

        var subjectTokens = SignificantTokens(subject);
        var claimTokens = SignificantTokens(claim);
        if (subjectTokens.Count == 0 || !subjectTokens.Any(claimTokens.Contains)) return false;

        if (DefinitionPredicateRegex().IsMatch(claim)) return true;

        // Configuration paths are commonly documented as compact catalogue entries rather than
        // grammatical "X is Y" sentences (for example "Reranking:External, disabled external
        // cross-encoder ..."). Accept that source-native definition only when the requested subject
        // is itself a configuration path and the claim adds meaningful descriptive tokens. A title
        // or a bare key still fails this check.
        if (!subject.Contains(':', StringComparison.Ordinal)) return false;
        var normalizedSubject = NormalizeComparableText(subject);
        var normalizedClaim = NormalizeComparableText(claim);
        if (!normalizedClaim.StartsWith(normalizedSubject + " ", StringComparison.Ordinal)) return false;
        return claimTokens.Except(subjectTokens).Count() >= 3;
    }

    private static string? DefinitionSubject(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return null;
        var match = TurkishDefinitionQuestionRegex().Match(question.Trim());
        if (!match.Success) match = EnglishDefinitionQuestionRegex().Match(question.Trim());
        return match.Success ? match.Groups["subject"].Value.Trim() : null;
    }

    private static bool SameNormalizedText(string left, string right) =>
        NormalizeComparableText(left) == NormalizeComparableText(right);

    private static string NormalizeComparableText(string value) => string.Join(' ',
        SlugHelper.Transliterate(CitationRegex().Replace(MarkdownPrefixRegex().Replace(value.Trim(), ""), " "))
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<string> SupportWindows(string passage)
    {
        foreach (var sentence in EvidenceSentences(passage))
        {
            // Keep the full sentence for claims whose support spans adjacent clauses, then add
            // contrast-separated clauses so an unrelated "ancak ... desteklenmez" predicate does
            // not contaminate the polarity check for the matching clause.
            yield return sentence;
            foreach (var clause in ClauseBoundaryRegex().Split(sentence)
                         .Select(x => x.Trim()).Where(x => x.Length >= 3 && x != sentence))
                yield return clause;
        }
    }

    private static IEnumerable<string> EvidenceSentences(string passage) =>
        SentenceBoundaryRegex().Split(passage)
            .Select(sentence => MarkdownPrefixRegex().Replace(sentence.Trim(), "").Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence));

    private static bool IsLexicallySupported(string claim, string supportWindow)
    {
        var claimTokens = SignificantTokens(claim);
        if (claimTokens.Count == 0) return false;
        var passageTokens = SignificantTokens(supportWindow);
        if (claimTokens.Count(claimToken => passageTokens.Any(passageToken =>
                InflectionAwareTokenMatch(claimToken, passageToken))) / (double)claimTokens.Count < MinimumLexicalSupport)
            return false;

        var claimNumbers = NumberRegex().Matches(claim).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var passageNumbers = NumberRegex().Matches(supportWindow).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        if (!claimNumbers.IsSubsetOf(passageNumbers)) return false;

        // A lexical overlap score alone treats "must" and "must not" as equivalent. Reject the
        // claim when explicit English/Turkish negation polarity differs from its local support.
        return HasNegation(claim) == HasNegation(supportWindow);
    }

    private static bool InflectionAwareTokenMatch(string left, string right)
    {
        if (left == right) return true;

        // Turkish suffixes frequently turn a supported source word into a different surface token
        // (for example "sağlayan"/"sağlar" or "çağırmayı"/"çağırmasını"). Accept only a long,
        // dominant shared stem; number and negation consistency are still checked separately below.
        var shorter = Math.Min(left.Length, right.Length);
        if (shorter < 5) return false;
        var common = 0;
        while (common < shorter && left[common] == right[common]) common++;
        return common >= 5 && common / (double)shorter >= .7;
    }

    private static bool HasNegation(string text) => NegationRegex().IsMatch(SlugHelper.Transliterate(text).ToLowerInvariant());

    private static string NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "summary" => "summary",
        "step" => "step",
        "constraint" => "constraint",
        "exception" => "exception",
        "conflict" => "conflict",
        _ => "explanation"
    };

    private static List<RagClaim> NormalizeClaimOrder(IReadOnlyCollection<RagClaim> claims)
    {
        var ordered = claims.ToList();
        if (ordered.Count == 0) return ordered;
        for (var index = 0; index < ordered.Count; index++)
        {
            var role = index == 0 ? "summary" : NormalizeRole(ordered[index].Role);
            if (index > 0 && role == "summary") role = "explanation";
            ordered[index] = ordered[index] with { Role = role };
        }
        return ordered;
    }

    private static bool LooksEnglish(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return false;
        var normalized = SlugHelper.Transliterate(question).ToLowerInvariant();
        return EnglishQuestionWordRegex().IsMatch(normalized) && !TurkishQuestionWordRegex().IsMatch(normalized);
    }

    private static HashSet<string> SignificantTokens(string text)
    {
        var stop = new HashSet<string>(StringComparer.Ordinal)
        {
            "ve", "veya", "ile", "için", "bir", "bu", "şu", "da", "de", "nedir", "demektir", "anlama", "gelir",
            "the", "a", "an", "and", "or", "for", "to", "of", "is", "are", "what", "define", "means"
        };
        return SlugHelper.Transliterate(text).ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3 && !stop.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\[(S\d+)\]")]
    private static partial Regex CitationRegex();
    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\b")]
    private static partial Regex NumberRegex();
    [GeneratedRegex(@"\b(?:not|never|no|degil|yok|hayir)\b|mamal[ıi]|memeli|maz\b|mez\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegationRegex();
    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeFenceRegex();
    [GeneratedRegex(@"<think\b[^>]*>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRegex();
    [GeneratedRegex(@"^(?:#{1,6}\s+|[-*+>]\s+|\d+[.)]\s+)")]
    private static partial Regex MarkdownPrefixRegex();
    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n+")]
    private static partial Regex SentenceBoundaryRegex();
    [GeneratedRegex(@"\s*(?:;|\b(?:ama|ancak|fakat|lakin|but|however)\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseBoundaryRegex();
    [GeneratedRegex(@"^\s*(?<subject>.+?)\s+(?:nedir|ne\s+demektir|ne\s+anlama\s+gelir)\s*[?!.]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishDefinitionQuestionRegex();
    [GeneratedRegex(@"^\s*(?:what\s+is|what's|define)\s+(?<subject>.+?)\s*[?!.]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishDefinitionQuestionRegex();
    [GeneratedRegex(@"\b(?:bir|olarak|anlamına\s+gelir|ifade\s+eder|kısaltmasıdır|is|means|refers\s+to|stands\s+for|denotes)\b|\p{L}{3,}(?:d[ıiuü]r|t[ıiuü]r)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DefinitionPredicateRegex();
    [GeneratedRegex(@"\s+(?=[\p{L}][\p{L}\p{N}_]*(?::[\p{L}\p{N}_*]+)+)")]
    private static partial Regex NextConfigurationKeyRegex();
    [GeneratedRegex(@"\b(?:what|how|why|when|where|which|explain|summarize|compare|overview)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishQuestionWordRegex();
    [GeneratedRegex(@"\b(?:nedir|nasil|neden|nicin|ne|hangi|acikla|ozetle|karsilastir)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishQuestionWordRegex();
}
