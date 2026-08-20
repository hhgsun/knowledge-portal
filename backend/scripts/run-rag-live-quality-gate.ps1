param(
    [int]$PollSeconds = 2,
    [int]$MaxWaitSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$baseUrl = $env:RAG_LIVE_BASE_URL
$token = $env:RAG_LIVE_TOKEN
$datasetId = $env:RAG_LIVE_DATASET_ID

if ([string]::IsNullOrWhiteSpace($baseUrl) -or
    [string]::IsNullOrWhiteSpace($token) -or
    [string]::IsNullOrWhiteSpace($datasetId)) {
    throw 'RAG_LIVE_BASE_URL, RAG_LIVE_TOKEN and RAG_LIVE_DATASET_ID are mandatory for the live RAG quality gate.'
}

$headers = @{ Authorization = "Bearer $token" }
$root = $baseUrl.TrimEnd('/')
$queued = Invoke-RestMethod -Method Post -Uri "$root/api/admin/rag-evaluations/datasets/$datasetId/runs" -Headers $headers
if ([string]::IsNullOrWhiteSpace($queued.id)) {
    throw 'The live RAG evaluation endpoint did not return a run id.'
}

$deadline = [DateTime]::UtcNow.AddSeconds($MaxWaitSeconds)
do {
    Start-Sleep -Seconds $PollSeconds
    $run = Invoke-RestMethod -Method Get -Uri "$root/api/admin/rag-evaluations/runs/$($queued.id)" -Headers $headers
    if ($run.status -eq 'failed') { throw "Live RAG evaluation failed: $($run.error)" }
    if ($run.status -eq 'completed') {
        if (-not $run.metrics.passed) {
            $failed = $run.metrics.failedGates -join '; '
            throw "Live RAG quality gates failed: $failed"
        }
        Write-Host "Live RAG quality gate passed: run=$($queued.id), cases=$($run.totalCases), p95=$($run.metrics.p95LatencyMs)ms"
        exit 0
    }
} while ([DateTime]::UtcNow -lt $deadline)

throw "Live RAG quality gate timed out after $MaxWaitSeconds seconds."
