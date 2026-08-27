using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace KnowledgePortal.Api.Services;

public sealed record CachedAssistantAnswer(string Answer, AssistantRagDto Rag);

public sealed class AssistantAnswerCacheService(AppDbContext db, IServiceProvider services,
    IConfiguration config, PortalMetrics metrics)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private float[]? currentEmbedding;
    private string? currentQueryFingerprint;

    public async Task<CachedAssistantAnswer?> TryGetAsync(string query, ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (!config.GetValue("Assistant:SemanticCache:Enabled", true)
            || services.GetService<IEmbeddingGenerator<string, Embedding<float>>>() is not { } generator)
            return null;
        var embedding = await EmbedAsync(query, generator, ct);
        currentEmbedding = embedding; currentQueryFingerprint = Fingerprint(query);
        var scope = UserScope(principal);
        var corpus = await CorpusFingerprintAsync(ct); var runtime = RuntimeFingerprint();
        var candidates = await db.AssistantAnswerCacheEntries
            .Where(x => x.UserScope == scope && x.ExpiresAt > DateTime.UtcNow
                && x.CorpusFingerprint == corpus && x.RuntimeFingerprint == runtime)
            .OrderByDescending(x => x.LastHitAt).Take(300).ToListAsync(ct);
        var threshold = Math.Clamp(config.GetValue("Assistant:SemanticCache:SimilarityThreshold", .94), .8, 1);
        var best = candidates.Select(x => (Entry: x, Score: Cosine(embedding,
                JsonSerializer.Deserialize<float[]>(x.QueryEmbeddingJson, Json) ?? [])))
            .OrderByDescending(x => x.Score).FirstOrDefault();
        if (best.Entry == null || best.Score < threshold)
        {
            metrics.AssistantAnswerCache.Add(1,
                new KeyValuePair<string, object?>("outcome", "miss")); return null;
        }
        best.Entry.HitCount++; best.Entry.LastHitAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        metrics.AssistantAnswerCache.Add(1,
            new KeyValuePair<string, object?>("outcome", "hit"));
        return JsonSerializer.Deserialize<CachedAssistantAnswer>(best.Entry.AnswerJson, Json);
    }

    public async Task StoreAsync(string query, ClaimsPrincipal principal, CachedAssistantAnswer answer,
        CancellationToken ct)
    {
        if (!config.GetValue("Assistant:SemanticCache:Enabled", true)
            || answer.Rag.InsufficientContext || answer.Rag.PartialResult
            || answer.Rag.CitationCoverage < config.GetValue("Assistant:SemanticCache:MinimumCitationCoverage", .9)
            || services.GetService<IEmbeddingGenerator<string, Embedding<float>>>() is not { } generator) return;
        var fingerprint = Fingerprint(query);
        var embedding = currentQueryFingerprint == fingerprint && currentEmbedding != null
            ? currentEmbedding : await EmbedAsync(query, generator, ct);
        var scope = UserScope(principal); var corpus = await CorpusFingerprintAsync(ct);
        var runtime = RuntimeFingerprint();
        var existing = await db.AssistantAnswerCacheEntries.FirstOrDefaultAsync(x =>
            x.UserScope == scope && x.QueryFingerprint == fingerprint
            && x.CorpusFingerprint == corpus && x.RuntimeFingerprint == runtime, ct);
        var ttl = Math.Clamp(config.GetValue("Assistant:SemanticCache:TtlMinutes", 60), 1, 1440);
        if (existing == null)
        {
            existing = new AssistantAnswerCacheEntry { UserId = principal.GetUserId(), UserScope = scope,
                QueryFingerprint = fingerprint,
                QueryEmbeddingJson = JsonSerializer.Serialize(embedding, Json), CorpusFingerprint = corpus,
                RuntimeFingerprint = runtime, AnswerJson = JsonSerializer.Serialize(answer, Json) };
            db.AssistantAnswerCacheEntries.Add(existing);
        }
        else existing.AnswerJson = JsonSerializer.Serialize(answer, Json);
        existing.ExpiresAt = DateTime.UtcNow.AddMinutes(ttl); existing.LastHitAt = DateTime.UtcNow;
        var expired = db.AssistantAnswerCacheEntries.Where(x => x.UserScope == scope
            && x.ExpiresAt <= DateTime.UtcNow && x.Id != existing.Id);
        if (db.Database.IsRelational()) await expired.ExecuteDeleteAsync(ct);
        else db.AssistantAnswerCacheEntries.RemoveRange(await expired.ToListAsync(ct));
        await db.SaveChangesAsync(ct); metrics.AssistantAnswerCache.Add(1,
            new KeyValuePair<string, object?>("outcome", "store"));
    }

    private async Task<string> CorpusFingerprintAsync(CancellationToken ct)
    {
        var rows = await db.Articles.AsNoTracking().Where(x => x.Status == "published")
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.UpdatedAt, x.IndexedAt, x.ApprovedAt,
                x.LastReviewedAt }).ToListAsync(ct);
        var authority = await db.LookupValues.AsNoTracking().Where(x => x.Category == "content_type")
            .OrderBy(x => x.Value).Select(x => new { x.Value, x.AuthorityWeight, x.IsActive }).ToListAsync(ct);
        return Fingerprint(string.Join('\n', rows.Select(x =>
            $"{x.Id}|{x.UpdatedAt:o}|{x.IndexedAt:o}|{x.ApprovedAt:o}|{x.LastReviewedAt:o}"))
            + "\n" + string.Join('\n', authority.Select(x => $"{x.Value}|{x.AuthorityWeight}|{x.IsActive}")));
    }

    private string RuntimeFingerprint() => Fingerprint(string.Join('|', RagService.PromptVersion,
        RagService.RetrievalVersion, config["Ollama:ChatModel"], config["Ollama:EmbeddingModel"],
        config["Ollama:ChunkingVersion"]));
    private static string UserScope(ClaimsPrincipal p) =>
        $"{p.GetUserId()}|{p.GetRole()}|{p.GetSource()}|{p.GetApiKeyId() ?? "session"}";
    private static string Fingerprint(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();
    private static async Task<float[]> EmbedAsync(string query,
        IEmbeddingGenerator<string, Embedding<float>> generator, CancellationToken ct) =>
        (await generator.GenerateAsync([query], cancellationToken: ct)).First().Vector.ToArray();
    private static double Cosine(float[] left, float[] right)
    {
        if (left.Length == 0 || left.Length != right.Length) return -1;
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < left.Length; i++) { dot += left[i] * right[i]; a += left[i] * left[i]; b += right[i] * right[i]; }
        return a == 0 || b == 0 ? -1 : dot / Math.Sqrt(a * b);
    }
}
