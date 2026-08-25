using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KnowledgePortal.Api.Tests.Integration;

internal static class McpTestClient
{
    private const string ModernProtocolVersion = "2026-07-28";

    public static void AddAcceptHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, object body)
    {
        var node = JsonSerializer.SerializeToNode(body) ?? throw new InvalidOperationException("MCP body is null.");
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(node)
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        if (node is not JsonObject envelope
            || envelope["method"] is not JsonValue methodValue
            || !methodValue.TryGetValue<string>(out var method))
            return client.SendAsync(request);

        if (method == "initialize")
        {
            var parameters = envelope["params"] as JsonObject ?? new JsonObject();
            parameters["protocolVersion"] ??= "2025-11-25";
            parameters["capabilities"] ??= new JsonObject();
            parameters["clientInfo"] ??= new JsonObject
            {
                ["name"] = "knowledge-portal-tests",
                ["version"] = "1.0.0"
            };
            envelope["params"] = parameters;
            request.Content = JsonContent.Create(envelope);
            return client.SendAsync(request);
        }

        // Notifications keep the negotiated legacy shape. Requests use the modern,
        // self-contained envelope so no session or initialize round-trip is required.
        if (!envelope.ContainsKey("id"))
            return client.SendAsync(request);

        if (envelope["params"] is JsonObject modernParams)
        {
            modernParams["_meta"] = ModernMeta();
        }
        else if (!envelope.ContainsKey("params"))
        {
            envelope["params"] = new JsonObject { ["_meta"] = ModernMeta() };
        }

        request.Headers.Add("MCP-Protocol-Version", ModernProtocolVersion);
        request.Headers.Add("Mcp-Method", method);
        if (method == "tools/call"
            && envelope["params"] is JsonObject callParams
            && callParams["name"] is JsonValue nameValue
            && nameValue.TryGetValue<string>(out var toolName))
        {
            request.Headers.Add("Mcp-Name", toolName);
        }
        request.Content = JsonContent.Create(envelope);
        return client.SendAsync(request);
    }

    public static async Task<JsonElement> ReadEnvelopeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        if (payload.TrimStart().StartsWith('{'))
            return JsonSerializer.Deserialize<JsonElement>(payload);

        var data = payload.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal))?[5..].Trim();
        if (string.IsNullOrWhiteSpace(data))
            throw new JsonException($"MCP response did not contain a JSON or SSE data payload: {payload[..Math.Min(120, payload.Length)]}");
        return JsonSerializer.Deserialize<JsonElement>(data);
    }

    private static JsonObject ModernMeta() => new()
    {
        ["io.modelcontextprotocol/protocolVersion"] = ModernProtocolVersion,
        ["io.modelcontextprotocol/clientInfo"] = new JsonObject
        {
            ["name"] = "knowledge-portal-tests",
            ["version"] = "1.0.0"
        },
        ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject()
    };
}
