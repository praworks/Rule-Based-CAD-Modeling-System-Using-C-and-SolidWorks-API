$key = [Environment]::GetEnvironmentVariable('GROQ_API_KEY','User')
if (-not $key) { Write-Output 'GROQ_API_KEY not set'; exit 1 }
$model = [Environment]::GetEnvironmentVariable('GROQ_MODEL','User')
if (-not $model) { $model = 'llama-3.3-70b-versatile' }
# Feature JSON from prior decompose
$featureJson = @'
{"description":"cube","needs_description":false,"question":"","features":[{"feature_type":"extrude","role":"base","intent":"create cube","depends_on":[]} ]}
'@
# Parse catalog
$j = Get-Content 'Config\PromptCatalog.json' -Raw | ConvertFrom-Json
# Choose system prompt: feature-specific if available
$featType = 'extrude'
$byFeatureKey = "execute_$featType"
$sys = $j.systemPromptsByFeature.$byFeatureKey
if (-not $sys) { $sys = $j.systemPrompts.execute_system }
# Template
$tmpl = $j.templates.execute_template
# facts empty
$factsSection = ''
$featureIndex = 0
# Feature task: use the first feature object only
$parsed = $featureJson | ConvertFrom-Json
$featureTask = $parsed.features[0] | ConvertTo-Json -Depth 10
# Build prompt
$prompt = $tmpl -replace '\{systemPrompt\}',$sys
$prompt = $prompt -replace '\{factsSection\}',$factsSection
$prompt = $prompt -replace '\{featureIndex\}',$featureIndex
$prompt = $prompt -replace '\{featureTask\}',$featureTask
Write-Output '--- EXECUTE prompt ---'
Write-Output $prompt
# Send to Groq
$payload = @{ model = $model; messages = @(@{ role='system'; content = $sys }, @{ role='user'; content = $prompt }); temperature = 0.1; max_tokens = 1024; stream = $false }
$json = $payload | ConvertTo-Json -Depth 10
Write-Output 'Sending to Groq endpoint https://api.groq.com/openai/v1/chat/completions'
try {
    $headers = @{ Authorization = "Bearer $key" }
    $resp = Invoke-RestMethod -Uri 'https://api.groq.com/openai/v1/chat/completions' -Method Post -Body $json -ContentType 'application/json' -Headers $headers -TimeoutSec 120
    Write-Output 'Response:'
    $resp | ConvertTo-Json -Depth 10
} catch {
    Write-Output 'Request failed:'
    if ($_.Exception -and $_.Exception.Response) {
        try { $code = $_.Exception.Response.StatusCode.Value__ } catch { $code = 'unknown' }
        Write-Output "HTTP status: $code"
        try { $body = $_.Exception.Response.GetResponseStream() | %{ new-object System.IO.StreamReader($_) } | %{ $_.ReadToEnd() } ; Write-Output "Body: $body" } catch { }
    } else { $_ | Out-String }
}
