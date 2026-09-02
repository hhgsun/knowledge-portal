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
$root = $baseUrl.TrimEnd('/')
$conversation = $null
try {
    $conversation = Invoke-RestMethod -Method Post -Uri "$root/api/assistant/conversations" `
        -Headers $headers -ContentType 'application/json'
    $body = @{ message = $question; conversationId = $conversation.id } | ConvertTo-Json -Depth 5
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

    $followUpBody = @{ message = 'sırala'; conversationId = $conversation.id } | ConvertTo-Json -Depth 5
    $followUp = Invoke-RestMethod -Method Post -Uri "$root/api/assistant" -Headers $headers `
        -ContentType 'application/json' -Body $followUpBody
    if ($followUp.intent -ne 'list' -or $followUp.presentation -ne 'ordered_list') {
        throw "Live Assistant lost the follow-up task: intent=$($followUp.intent), presentation=$($followUp.presentation)"
    }
    if (-not (@($followUp.toolCalls) -contains 'conversation_transform')) {
        throw "Live Assistant performed the wrong follow-up action: $(@($followUp.toolCalls) -join ', ')"
    }
    if ($followUp.normalizedQuery -ne $response.normalizedQuery -or $null -eq $followUp.rag) {
        throw 'Live Assistant did not preserve the previous grounded knowledge state.'
    }
    if (-not (@($followUp.contentBlocks).type -contains 'ordered_list')) {
        throw 'Live Assistant did not return the ordered-list content block.'
    }

    Write-Host "Live Assistant turn-planning gate passed: grounding=$($response.rag.groundingStatus), sources=$(@($response.rag.sources).Count)"
}
finally {
    if ($null -ne $conversation -and -not [string]::IsNullOrWhiteSpace($conversation.id)) {
        Invoke-RestMethod -Method Delete -Uri "$root/api/assistant/conversations/$($conversation.id)" `
            -Headers $headers -ErrorAction SilentlyContinue | Out-Null
    }
}
