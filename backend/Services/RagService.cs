using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace KnowledgePortal.Api.Services;

public class RagService(
    IChatClient chatClient,
    VectorSearchService vectorSearch,
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<RagService> logger)
{
    private const int DefaultContextLimit = 5;
    private const int MaxContextWords = 3000;

    private static readonly string SystemPrompt = """
        You are a Knowledge Portal assistant.
        Rules:
        - Answer ONLY based on the provided context below
        - If context is insufficient, say "Bu konuda yeterli bilgi bulamadım."
        - Cite sources by [Article Title] format
        - Respond in the same language as the question
        - Be concise and factual
        - Do not make up information
        """;

    public record RagResult(string Answer, List<RagSource> Sources);
    public record RagSource(string ArticleId, string Title, string Slug, double Score);

    public async Task<RagResult> AskAsync(string question, CancellationToken ct = default)
    {
        var searchResults = await vectorSearch.SearchAsync(question, DefaultContextLimit, ct);

        if (searchResults.Count == 0)
        {
            return new RagResult(
                "Henüz indexlenmiş makale bulunamadı. Lütfen daha sonra tekrar deneyin.", []);
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var articleIds = searchResults.Select(r => r.ArticleId).ToList();
        var articles = await db.Articles
            .Where(a => articleIds.Contains(a.Id) && a.Status == "published")
            .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.Content })
            .ToListAsync(ct);

        if (articles.Count == 0)
            return new RagResult("İlgili makale bulunamadı.", []);

        var contextParts = new List<string>();
        var sources = new List<RagSource>();
        var totalWords = 0;

        foreach (var sr in searchResults)
        {
            var article = articles.FirstOrDefault(a => a.Id == sr.ArticleId);
            if (article == null) continue;

            var availableWords = MaxContextWords - totalWords;
            if (availableWords <= 0) break;

            // Rebuild the exact text that was chunked at embedding time (attachments included)
            // and use the chunk that actually matched the query as context.
            var attachmentText = await AttachmentHelper.GetAttachmentTextAsync(db, config, article.Id, ct);
            var text = ContentExtractor.ExtractSearchableText(article.Title, article.Excerpt, article.Content, attachmentText);
            var chunks = EmbeddingService.ChunkText(text);
            var chunkText = chunks[Math.Clamp(sr.ChunkIndex, 0, chunks.Count - 1)];

            var words = chunkText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var truncatedText = words.Length > availableWords
                ? string.Join(' ', words.Take(availableWords))
                : chunkText;

            contextParts.Add($"[{article.Title}]\n{truncatedText}");
            sources.Add(new RagSource(article.Id, article.Title, article.Slug, sr.Score));
            totalWords += Math.Min(words.Length, availableWords);
        }

        var userMessage = $"""
            Question: {question}

            Context:
            {string.Join("\n\n", contextParts)}
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userMessage)
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var answer = response.Text ?? "Yanıt oluşturulamadı.";
            logger.LogInformation("RAG answer generated for query '{Query}' with {SourceCount} sources",
                question[..Math.Min(50, question.Length)], sources.Count);
            return new RagResult(answer, sources);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate RAG answer for query '{Query}'", question);
            return new RagResult("AI yanıtı oluşturulurken bir hata oluştu. Lütfen daha sonra tekrar deneyin.", sources);
        }
    }
}
