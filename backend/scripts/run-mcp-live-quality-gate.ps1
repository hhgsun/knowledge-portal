param()

$ErrorActionPreference = 'Stop'
$baseUrl = $env:MCP_LIVE_BASE_URL
$token = $env:MCP_LIVE_TOKEN
$question = $env:MCP_LIVE_QUESTION
$expectedSourceSlug = $env:MCP_LIVE_EXPECTED_SOURCE_SLUG

if ([string]::IsNullOrWhiteSpace($baseUrl) -or [string]::IsNullOrWhiteSpace($token)) {
    throw 'MCP_LIVE_BASE_URL and MCP_LIVE_TOKEN are mandatory for the live MCP quality gate.'
}
if ([string]::IsNullOrWhiteSpace($question) -or $question.StartsWith('$(')) {
    $question = 'Knowledge Portal MCP entegrasyonu nasıl çalışır?'
}
if (-not [string]::IsNullOrWhiteSpace($expectedSourceSlug) -and $expectedSourceSlug.StartsWith('$(')) {
    $expectedSourceSlug = $null
}

$protocolVersion = '2026-07-28'
$headers = @{
    Authorization = "Bearer $token"
    Accept = 'application/json, text/event-stream'
    'MCP-Protocol-Version' = $protocolVersion
    'Mcp-Method' = 'tools/call'
    'Mcp-Name' = 'ask_knowledge'
}
$body = @{
    jsonrpc = '2.0'
    id = "live-mcp-$([Guid]::NewGuid().ToString('N'))"
    method = 'tools/call'
    params = @{
        name = 'ask_knowledge'
        arguments = @{ question = $question }
        _meta = @{
            'io.modelcontextprotocol/protocolVersion' = $protocolVersion
            'io.modelcontextprotocol/clientInfo' = @{ name = 'knowledge-portal-live-gate'; version = '1.0.0' }
            'io.modelcontextprotocol/clientCapabilities' = @{}
        }
    }
} | ConvertTo-Json -Depth 10

$root = $baseUrl.TrimEnd('/')
$http = Invoke-WebRequest -Method Post -Uri "$root/mcp" -Headers $headers `
    -ContentType 'application/json' -Body $body
$payload = $http.Content
if (-not $payload.TrimStart().StartsWith('{')) {
    $dataLine = ($payload -split "`n" | Where-Object { $_.TrimStart().StartsWith('data:') } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($dataLine)) { throw 'Live MCP response was neither JSON nor a valid SSE data event.' }
    $payload = $dataLine.Substring($dataLine.IndexOf('data:') + 5).Trim()
}
$envelope = $payload | ConvertFrom-Json -Depth 100
if ($null -ne $envelope.error) { throw "Live MCP protocol error: $($envelope.error.code) $($envelope.error.message)" }
if ($envelope.result.isError) {
    throw "Live MCP tool error: $($envelope.result.structuredContent.error.code) $($envelope.result.structuredContent.error.message)"
}
$result = $envelope.result.structuredContent
if ([string]::IsNullOrWhiteSpace($result.answer) -or $null -eq $result.evidence) {
    throw 'Live MCP ask_knowledge response did not contain answer and evidence fields.'
}
if ([string]::IsNullOrWhiteSpace($result.traceId)) {
    throw 'Live MCP ask_knowledge response did not contain a RAG trace id.'
}
if (-not [string]::IsNullOrWhiteSpace($expectedSourceSlug) -and
    -not (@($result.sources.slug) -contains $expectedSourceSlug)) {
    throw "Live MCP response did not cite expected source slug '$expectedSourceSlug'."
}
if ([string]::IsNullOrWhiteSpace($http.Headers['X-Trace-Id'])) {
    throw 'Live MCP HTTP response did not contain X-Trace-Id.'
}

Write-Host "Live MCP quality gate passed: grounding=$($result.groundingStatus), sources=$(@($result.sources).Count), evidence=$(@($result.evidence).Count)"
