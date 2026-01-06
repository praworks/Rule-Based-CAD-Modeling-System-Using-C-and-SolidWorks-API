# Probe LLM providers in configured priority order
# Reads: AICAD_LLM_PRIORITY, LOCAL_LLM_ENDPOINT, GEMINI_API_KEY, GROQ_API_KEY

function Try-Local {
    param($endpoint)
    if (-not $endpoint) { Write-Host "  Local endpoint not set"; return }
    Write-Host "  Testing Local endpoint: $endpoint"
    try {
        $r = Invoke-WebRequest -Uri $endpoint -UseBasicParsing -Method Get -TimeoutSec 5 -ErrorAction Stop
        Write-Host "    [OK] HTTP $($r.StatusCode)"
        return
    } catch {
        Write-Host "    [ERR] $($_.Exception.Message)"
    }
    # try common models path
    try {
        $models = if ($endpoint.EndsWith('/')) { $endpoint + 'v1/models' } else { $endpoint + '/v1/models' }
        $r2 = Invoke-WebRequest -Uri $models -UseBasicParsing -Method Get -TimeoutSec 5 -ErrorAction Stop
        Write-Host "    [OK] models path HTTP $($r2.StatusCode)"
        return
    } catch {
        Write-Host "    [ERR] models path: $($_.Exception.Message)"
    }
}

$priority = $env:AICAD_LLM_PRIORITY
if (-not $priority) { $priority = 'local,gemini,groq' }
$providers = $priority.Split(',') | ForEach-Object { $_.Trim().ToLower() }
Write-Host "Provider priority: $priority"

foreach ($p in $providers) {
    Write-Host "- Provider: $p"
    switch ($p) {
        'local' {
            $local = $env:LOCAL_LLM_ENDPOINT
            if (-not $local) { $local = 'http://127.0.0.1:1234' }
            Try-Local -endpoint $local
        }
        'gemini' {
            if ($env:GEMINI_API_KEY) { Write-Host "  Gemini: API key present (will attempt cloud calls when running app)." } else { Write-Host "  Gemini: API key NOT configured." }
        }
        'groq' {
            if ($env:GROQ_API_KEY) { Write-Host "  Groq: API key present (will attempt cloud calls when running app)." } else { Write-Host "  Groq: API key NOT configured." }
        }
        default {
            Write-Host "  Unknown provider: $p"
        }
    }
}

Write-Host "Probe complete."