using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public record RagRetrievalChunk(VectorChunkResult Chunk, double Score, string MatchType);

public interface IRagRetriever
{
    Task<List<RagRetrievalChunk>> RetrieveAsync(RagQueryPlan plan, int limit, double minSemanticScore,
        int maxPerArticle, ArticleFilter? filter = null, CancellationToken ct = default);
}

public record RagChunkCandidate(VectorChunkResult Chunk, string Title, string? Excerpt,
    double RetrievalScore, string MatchType);

public interface IRagChunkReranker
{
    Task<IReadOnlyList<RagRetrievalChunk>> RerankAsync(string query,
        IReadOnlyList<RagChunkCandidate> candidates, CancellationToken ct = default);
}

/// <summary>Local, deterministic second-stage chunk reranker. The contract can be replaced by a
/// cross-encoder without changing RAG orchestration.</summary>
public sealed class LocalRagChunkReranker : IRagChunkReranker
{
    public Task<IReadOnlyList<RagRetrievalChunk>> RerankAsync(string query,
        IReadOnlyList<RagChunkCandidate> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0) return Task.FromResult<IReadOnlyList<RagRetrievalChunk>>([]);
        var tokens = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        var maxRetrieval = Math.Max(candidates.Max(x => x.RetrievalScore), double.Epsilon);
        IReadOnlyList<RagRetrievalChunk> result = candidates.Select(x =>
        {
            var title = Fold(x.Title); var body = Fold(x.Chunk.ChunkText); var source = Fold(x.Chunk.SourceName ?? "");
            var haystack = $"{title} {source} {Fold(x.Excerpt ?? "")} {body}";
            var coverage = tokens.Length == 0 ? 0 : tokens.Count(haystack.Contains) / (double)tokens.Length;
            var titleCoverage = tokens.Length == 0 ? 0 : tokens.Count(t => title.Contains(t) || source.Contains(t)) / (double)tokens.Length;
            var phrase = query.Length > 1 && haystack.Contains(Fold(query), StringComparison.Ordinal) ? 1d : 0d;
            var score = .50 * (x.RetrievalScore / maxRetrieval) + .25 * coverage + .20 * titleCoverage + .05 * phrase;
            return new RagRetrievalChunk(x.Chunk, score, x.MatchType);
        }).OrderByDescending(x => x.Score).ThenBy(x => x.Chunk.ArticleId).ThenBy(x => x.Chunk.ChunkIndex).ToList();
        return Task.FromResult(result);
    }
    private static string Fold(string value) => SlugHelper.Transliterate(value).ToLowerInvariant();
}

/// <summary>Hybrid RAG retrieval: lexical and vector candidate generation, article-level RRF,
/// chunk reranking, near-duplicate suppression and fair per-article interleaving.</summary>
public sealed class HybridRagRetriever(
    IVectorSearchService vectors,
    FullTextSearchService fullText,
    AppDbContext db,
    IRagChunkReranker reranker,
    IConfiguration config,
    ILogger<HybridRagRetriever> logger) : IRagRetriever
{
    private readonly int _rrfK = Math.Max(1, config.GetValue("Ollama:RagRrfK", 60));
    private readonly double _lexicalWeight = Math.Clamp(config.GetValue("Ollama:RagLexicalWeight", .4), 0, 1);
    private readonly double _semanticWeight = Math.Clamp(config.GetValue("Ollama:RagSemanticWeight", .6), 0, 1);
    private readonly double _duplicateThreshold = Math.Clamp(config.GetValue("Ollama:RagDuplicateThreshold", .88), .5, 1);
    private readonly string _embeddingModel = config["Ollama:EmbeddingModel"] ?? "bge-m3";

    public async Task<List<RagRetrievalChunk>> RetrieveAsync(RagQueryPlan plan, int limit, double minSemanticScore,
        int maxPerArticle, ArticleFilter? filter = null, CancellationToken ct = default)
    {
        var perQuery = new List<List<RagRetrievalChunk>>();
        foreach (var query in plan.Queries)
            perQuery.Add(await RetrieveSingleAsync(query, limit, minSemanticScore, maxPerArticle,
                plan.EffectiveFilter ?? filter, plan.PrefersFreshSources, ct));

        if (perQuery.Count == 1) return perQuery[0];
        var merged = perQuery.SelectMany(list => list.Select((item, rank) => new { item, rank }))
            .GroupBy(x => Key(x.item.Chunk))
            .Select(group =>
            {
                var best = group.OrderByDescending(x => x.item.Score).First().item;
                var queryFusion = group.Sum(x => 1d / (_rrfK + x.rank + 1));
                return best with { Score = best.Score + queryFusion };
            })
            .OrderByDescending(x => x.Score).ToList();
        return InterleaveByArticle(merged, limit, maxPerArticle);
    }

    private async Task<List<RagRetrievalChunk>> RetrieveSingleAsync(string query, int limit,
        double minSemanticScore, int maxPerArticle, ArticleFilter? filter, bool prefersFreshSources,
        CancellationToken ct)
    {
        List<VectorChunkResult> semantic;
        try { semantic = await vectors.SearchChunksAsync(query, limit, ct, minSemanticScore, maxPerArticle, filter); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Semantic RAG retrieval failed; continuing with lexical candidates"); semantic = []; }

        List<string> lexicalIds;
        try
        {
            lexicalIds = db.Database.IsRelational()
                ? (await fullText.SearchPagedAsync(query, filter, 1, limit, ct)).ArticleIds
                : (await fullText.SearchInMemoryAsync(query, limit)).Select(x => x.ArticleId).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Lexical RAG retrieval failed; continuing with semantic candidates"); lexicalIds = []; }

        var semanticArticleIds = semantic.Select(x => x.ArticleId).Distinct().ToList();
        var fusion = RrfHelper.Merge(lexicalIds, semanticArticleIds, _rrfK, _lexicalWeight, _semanticWeight);
        var candidateIds = fusion.Keys.ToList();
        if (candidateIds.Count == 0) return [];

        var allowed = await ArticleService.ApplyFilter(db.Articles.WherePublished().Where(x => candidateIds.Contains(x.Id)), filter)
            .Select(x => new { x.Id, x.Title, x.Excerpt, x.Content, x.ContentType, x.UpdatedAt, x.ApprovedAt }).ToListAsync(ct);
        var metadata = allowed.ToDictionary(x => x.Id);
        var contentTypes = allowed.Select(x => x.ContentType).Distinct().ToList();
        var authorityByType = await db.LookupValues
            .Where(value => value.Category == "content_type" && contentTypes.Contains(value.Value))
            .ToDictionaryAsync(value => value.Value, value => value.AuthorityWeight, ct);

        var chunks = semantic.Where(x => metadata.ContainsKey(x.ArticleId)).ToList();
        var semanticKeys = chunks.Select(Key).ToHashSet();
        var lexicalCandidateIds = lexicalIds.Where(metadata.ContainsKey).ToList();
        var lexicalStoredChildren = await db.ArticleEmbeddings.AsNoTracking()
            .Where(x => lexicalCandidateIds.Contains(x.ArticleId) && x.ModelName == _embeddingModel
                && x.Content != null)
            .Select(x => new { x.Id, x.ArticleId, x.ChunkIndex, x.Content, x.SourceType,
                x.AttachmentId, x.SourceName, x.SourceLocation, x.ParentChunkId })
            .ToListAsync(ct);
        var queryTokens = Tokens(query);

        // Add provenance-bearing lexical passages even when the same article already has a
        // semantic hit: the exact FTS match may live in an attachment, not in that vector chunk.
        foreach (var articleId in lexicalCandidateIds)
        {
            var a = metadata[articleId];
            // Reuse persisted searchable children for lexical hits too. This keeps BM25/FTS and
            // vector retrieval on the same parent-child identities, so either path can resolve a
            // precise match to its larger parent without query-time re-chunking.
            var syntheticCandidates = lexicalStoredChildren.Where(x => x.ArticleId == articleId)
                .Select(x => new VectorChunkResult(x.ArticleId, x.ChunkIndex, 0, x.Content!,
                    x.SourceType, x.AttachmentId, x.SourceName, x.SourceLocation, x.Id,
                    x.ParentChunkId)).ToList();
            if (syntheticCandidates.Count == 0)
            {
                var text = ContentExtractor.ExtractSearchableText(a.Title, a.Excerpt, a.Content, "");
                syntheticCandidates.Add(SyntheticChunk(articleId, -1, text, "article", null,
                    a.Title, "article"));
            }

            foreach (var synthetic in syntheticCandidates
                .OrderByDescending(x => Tokens(x.ChunkText).Intersect(queryTokens).Count())
                .ThenByDescending(x => x.SourceType == "attachment")
                .Take(Math.Max(1, maxPerArticle)))
                if (semanticKeys.Add(Key(synthetic))) chunks.Add(synthetic);
        }

        var candidates = chunks.Select(chunk =>
        {
            var fused = fusion[chunk.ArticleId];
            var a = metadata[chunk.ArticleId];
            var ageDays = Math.Max(0, (DateTime.UtcNow - a.UpdatedAt).TotalDays);
            var halfLife = Math.Max(1, config.GetValue("Ollama:Ranking:FreshnessHalfLifeDays", 365));
            var freshness = Math.Pow(.5, ageDays / halfLife);
            var authority = authorityByType.GetValueOrDefault(a.ContentType, 50) / 100d
                + (a.ApprovedAt == null ? 0 : config.GetValue("Ollama:Ranking:ApprovedBoost", .2));
            var freshnessWeight = config.GetValue("Ollama:Ranking:FreshnessWeight", .05)
                * (prefersFreshSources ? config.GetValue("Ollama:Ranking:FreshnessIntentMultiplier", 3d) : 1d);
            var authorityWeight = config.GetValue("Ollama:Ranking:AuthorityWeight", .05);
            var retrieval = fused.Score + Math.Max(0, chunk.Score) * _semanticWeight
                + freshnessWeight * freshness + authorityWeight * Math.Clamp(authority, 0, 1);
            return new RagChunkCandidate(chunk, a.Title, a.Excerpt, retrieval, fused.MatchType);
        }).ToList();

        var ranked = await reranker.RerankAsync(query, candidates, ct);
        var deduped = SuppressNearDuplicates(ranked);
        return InterleaveByArticle(deduped, limit, maxPerArticle);
    }

    private List<RagRetrievalChunk> SuppressNearDuplicates(IReadOnlyList<RagRetrievalChunk> ranked)
    {
        var kept = new List<(RagRetrievalChunk Item, HashSet<string> Tokens)>();
        foreach (var item in ranked)
        {
            var tokens = Tokens(item.Chunk.ChunkText);
            if (kept.Any(x => x.Item.Chunk.ArticleId == item.Chunk.ArticleId && Jaccard(tokens, x.Tokens) >= _duplicateThreshold)) continue;
            kept.Add((item, tokens));
        }
        return kept.Select(x => x.Item).ToList();
    }

    internal static List<RagRetrievalChunk> InterleaveByArticle(List<RagRetrievalChunk> ranked, int limit, int maxPerArticle)
    {
        var groups = ranked.GroupBy(x => x.Chunk.ArticleId).Select(g => g.Take(Math.Max(1, maxPerArticle)).ToList()).ToList();
        var result = new List<RagRetrievalChunk>();
        for (var depth = 0; result.Count < limit && groups.Any(g => g.Count > depth); depth++)
            foreach (var group in groups)
                if (group.Count > depth && result.Count < limit) result.Add(group[depth]);
        return result;
    }

    private static string Key(VectorChunkResult x) => $"{x.ArticleId}:{x.SourceType}:{x.AttachmentId}:{x.ChunkIndex}";
    private static VectorChunkResult SyntheticChunk(string articleId, int chunkIndex, string content,
        string sourceType, string? attachmentId, string? sourceName, string? sourceLocation)
    {
        var identity = $"{articleId}|{sourceType}|{attachmentId}|{sourceLocation}|{content}";
        var chunkId = $"lex_{ContentExtractor.ComputeHash(identity)[..21]}";
        return new(articleId, chunkIndex, 0, content, sourceType, attachmentId, sourceName,
            sourceLocation, chunkId);
    }
    private static HashSet<string> Tokens(string text) => SlugHelper.Transliterate(text).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    private static double Jaccard(HashSet<string> a, HashSet<string> b) => a.Count == 0 && b.Count == 0 ? 1 : a.Intersect(b).Count() / (double)a.Union(b).Count();
}
