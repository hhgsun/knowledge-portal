using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgePortal.Api.Mcp;
using KnowledgePortal.Api.Services;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public class McpResilienceServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Timeout_ReturnsStructuredRetryableError()
    {
        var service = Build(("Mcp:Timeouts:list_tags", "1"));

        var result = await service.ExecuteAsync("list_tags", null,
            async ct => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Success(); }, CancellationToken.None);

        Assert.True(result.IsError);
        var error = result.StructuredContent!["error"]!;
        Assert.Equal("tool_timeout", error["code"]!.GetValue<string>());
        Assert.True(error["retryable"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentAiCall_ReturnsServerBusy()
    {
        var service = Build(("Mcp:AiConcurrencyLimit", "1"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = JsonSerializer.SerializeToElement(new { type = "semantic" });
        var first = service.ExecuteAsync("search_articles", args, async _ =>
        {
            started.SetResult(); await release.Task; return Success();
        }, CancellationToken.None);
        await started.Task;

        var second = await service.ExecuteAsync("search_articles", args, _ => Task.FromResult(Success()), CancellationToken.None);
        release.SetResult();
        await first;

        Assert.Equal("server_busy", second.StructuredContent!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_OversizedResult_ReturnsGuidanceError()
    {
        var service = Build(("Mcp:MaxOutputBytes", "16384"));
        var huge = new string('x', 20_000);

        var result = await service.ExecuteAsync("get_article", null,
            _ => Task.FromResult(new McpToolCallResult { StructuredContent = new JsonObject { ["content"] = huge }, Content = [new McpContent { Text = huge }] }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("output_too_large", result.StructuredContent!["error"]!["code"]!.GetValue<string>());
        Assert.False(result.StructuredContent!["error"]!["retryable"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExecuteAsync_RepeatedAiFailure_OpensCircuit()
    {
        var service = Build(("Mcp:CircuitBreakerFailureThreshold", "1"), ("Mcp:CircuitBreakerSeconds", "30"));
        var args = JsonSerializer.SerializeToElement(new { type = "semantic" });
        var unavailable = new McpToolCallResult
        {
            StructuredContent = new JsonObject { ["warning"] = "Semantic search failed" },
            Content = [new McpContent { Text = "Semantic search failed" }]
        };
        await service.ExecuteAsync("search_articles", args, _ => Task.FromResult(unavailable), CancellationToken.None);

        var next = await service.ExecuteAsync("search_articles", args, _ => Task.FromResult(Success()), CancellationToken.None);

        Assert.Equal("circuit_open", next.StructuredContent!["error"]!["code"]!.GetValue<string>());
        Assert.True(next.StructuredContent!["error"]!["retryAfterSeconds"]!.GetValue<int>() > 0);
    }

    private static McpResilienceService Build(params (string Key, string Value)[] values) => new(
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value)).Build());

    private static McpToolCallResult Success() => new()
    {
        StructuredContent = new JsonObject { ["ok"] = true },
        Content = [new McpContent { Text = "{\"ok\":true}" }]
    };
}
