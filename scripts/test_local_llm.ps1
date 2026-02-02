$endpoint = $env:LOCAL_LLM_ENDPOINT
if (-not $endpoint) { $endpoint = 'http://127.0.0.1:1234' }
if ($endpoint -notlike '*v1/chat/completions*') { if ($endpoint.EndsWith('/')) { $endpoint += 'v1/chat/completions' } else { $endpoint += '/v1/chat/completions' } }
Write-Output "Using endpoint: $endpoint"
$j = Get-Content 'Config\PromptCatalog.json' -Raw | ConvertFrom-Json
$sys = $j.systemPrompts.decompose_system
$tmpl = $j.templates.decompose_template
$user = 'Make a cube'
$prompt = $tmpl -replace '\{systemPrompt\}',$sys
$prompt = $prompt -replace '\{userRequest\}',$user
$messages = @()
if ($sys) { $messages += @{ role='system'; content = $sys } }
$messages += @{ role='user'; content = $prompt }
$payload = @{ messages = $messages; temperature = 0.0; stream = $false }
if ($env:LOCAL_LLM_MODEL) { $payload['model'] = $env:LOCAL_LLM_MODEL }
$json = $payload | ConvertTo-Json -Depth 10
Write-Output 'Request payload:'
Write-Output $json
try {
    $resp = Invoke-RestMethod -Uri $endpoint -Method Post -Body $json -ContentType 'application/json' -TimeoutSec 120
    Write-Output 'Response:'
    $resp | ConvertTo-Json -Depth 10
} catch {
    Write-Output 'Request failed:'
    $_ | Out-String
}
