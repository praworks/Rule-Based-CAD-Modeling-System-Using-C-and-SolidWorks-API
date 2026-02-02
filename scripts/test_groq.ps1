$key = [Environment]::GetEnvironmentVariable('GROQ_API_KEY','User')
if (-not $key) { Write-Output 'GROQ_API_KEY not set'; exit 1 }
$model = [Environment]::GetEnvironmentVariable('GROQ_MODEL','User')
if (-not $model) { $model = 'llama-3.3-70b-versatile' }
$j = Get-Content 'Config\PromptCatalog.json' -Raw | ConvertFrom-Json
$sys = $j.systemPrompts.decompose_system
$tmpl = $j.templates.decompose_template
$user = 'Make a cube'
$prompt = $tmpl -replace '\{systemPrompt\}',$sys
$prompt = $prompt -replace '\{userRequest\}',$user
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
