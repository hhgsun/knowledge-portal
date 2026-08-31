using System.Security.Claims;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace KnowledgePortal.Api.Tests.Unit;

public sealed class LlmModelSelectionServiceTests
{
    [Fact]
    public async Task Effective_model_prefers_user_choice_then_admin_default()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = "user-1", Name = "User", Slug = "user", Email = "user@example.test",
            PasswordHash = "hash", PreferredLlmModel = "model-b"
        });
        db.SystemSettings.Add(new SystemSetting
        {
            Key = LlmModelSelectionService.DefaultModelSettingKey, Value = "model-a"
        });
        await db.SaveChangesAsync();
        var config = Config();
        var service = new LlmModelSelectionService(db, config, Catalog(config));

        var settings = await service.GetSettingsAsync(Principal("user-1"));

        Assert.Equal("model-a", settings.DefaultModel);
        Assert.Equal("model-b", settings.PreferredModel);
        Assert.Equal("model-b", settings.EffectiveModel);
    }

    [Fact]
    public async Task Invalid_stored_values_fall_back_to_configured_default()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = "user-1", Name = "User", Slug = "user", Email = "user@example.test",
            PasswordHash = "hash", PreferredLlmModel = "removed-model"
        });
        db.SystemSettings.Add(new SystemSetting
        {
            Key = LlmModelSelectionService.DefaultModelSettingKey, Value = "removed-model"
        });
        await db.SaveChangesAsync();
        var config = Config();
        var service = new LlmModelSelectionService(db, config, Catalog(config));

        var settings = await service.GetSettingsAsync(Principal("user-1"));

        Assert.Equal("model-a", settings.DefaultModel);
        Assert.Null(settings.PreferredModel);
        Assert.Equal("model-a", settings.EffectiveModel);
    }

    [Fact]
    public async Task Catalog_discovers_completion_models_and_filters_embedding_models()
    {
        var config = Config();
        var result = await Catalog(config).GetAsync();

        Assert.Equal("ollama", result.Source);
        Assert.Equal(["model-a", "model-b"], result.Models.Select(x => x.Id));
        Assert.DoesNotContain(result.Models, x => x.Id == "bge-m3");
    }

    [Fact]
    public async Task Catalog_uses_configured_model_when_ollama_is_unavailable()
    {
        var config = Config();
        var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)))
        { BaseAddress = new Uri("http://ollama.test/") };
        var catalog = new OllamaModelCatalogService(client, config,
            NullLogger<OllamaModelCatalogService>.Instance);

        var result = await catalog.GetAsync();

        Assert.Equal("configured_fallback", result.Source);
        Assert.Equal("model-a", Assert.Single(result.Models).Id);
        Assert.NotNull(result.Warning);
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Ollama:ChatModel"] = "model-a",
            ["Ollama:EmbeddingModel"] = "bge-m3",
            ["Ollama:ModelCatalogCacheSeconds"] = "60"
        }).Build();

    private static OllamaModelCatalogService Catalog(IConfiguration config)
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal) == true)
                return Json("""
                    {"models":[
                      {"name":"model-b","model":"model-b","details":{"parameter_size":"8B","quantization_level":"Q4"}},
                      {"name":"bge-m3","model":"bge-m3","details":{"parameter_size":"567M"}},
                      {"name":"model-a","model":"model-a","details":{"parameter_size":"7B"}}
                    ]}
                    """);
            return Json("{\"capabilities\":[\"completion\"]}");
        });
        return new(new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test/") },
            config, NullLogger<OllamaModelCatalogService>.Instance);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private static ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity(
        [new Claim("id", id)], "test"));
}
