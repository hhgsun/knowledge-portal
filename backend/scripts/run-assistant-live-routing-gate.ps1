param()

$ErrorActionPreference = 'Stop'
$baseUrl = $env:ASSISTANT_LIVE_BASE_URL
$token = $env:ASSISTANT_LIVE_TOKEN
$question = $env:ASSISTANT_LIVE_QUESTION

if ([string]::IsNullOrWhiteSpace($baseUrl) -or [string]::IsNullOrWhiteSpace($token)) {
    throw 'ASSISTANT_LIVE_BASE_URL and ASSISTANT_LIVE_TOKEN are mandatory for the live Assistant routing gate.'
}
if ([string]::IsNullOrWhiteSpace($question) -or $question.StartsWith('$(')) {
    $question = 'Knowledge Portal MCP entegrasyonu nasıl çalışır?'
}

$headers = @{ Authorization = "Bearer $token" }
$body = @{ message = $question } | ConvertTo-Json -Depth 5
$root = $baseUrl.TrimEnd('/')
$response = Invoke-RestMethod -Method Post -Uri "$root/api/assistant" -Headers $headers `
    -ContentType 'application/json' -Body $body

if ([string]::IsNullOrWhiteSpace($response.answer)) {
    throw 'Live Assistant response did not contain an answer.'
}
if ($null -eq $response.rag -or $null -eq $response.rag.evidence) {
    throw 'Live Assistant response did not contain the grounded RAG contract.'
}
$toolCalls = @($response.toolCalls)
if (-not ($toolCalls -contains 'knowledge_rag' -or $toolCalls -contains 'semantic_answer_cache')) {
    throw "Live Assistant did not terminate in the grounded answer pipeline: $($toolCalls -join ', ')"
}
if ($null -ne $response.results -or $null -ne $response.analytics -or $null -ne $response.searchQueryId) {
    throw 'Live Assistant leaked a forbidden search, analytics, or routing payload.'
}

Write-Host "Live Assistant routing gate passed: grounding=$($response.rag.groundingStatus), sources=$(@($response.rag.sources).Count)"
