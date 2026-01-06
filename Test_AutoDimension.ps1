# Test script to verify auto_dimension prompt enforcement
$groqApiKey = $env:GROQ_API_KEY
if (-not $groqApiKey) {
    Write-Host "ERROR: GROQ_API_KEY not set" -ForegroundColor Red
    exit 1
}

Write-Host "Testing auto_dimension enforcement..." -ForegroundColor Cyan

$systemPrompt = "You are a CAD planning agent. Output only raw JSON with a top-level 'steps' array for SolidWorks. ALWAYS use op:'auto_dimension' for ALL sketch dimensions and include numeric fields cx, cy, w, h in mm. For dimension operations, you MUST copy the cx, cy, w, h values from the rectangle. No extra text."

$payload = @{
    model = "llama-3.3-70b-versatile"
    messages = @(
        @{ role = "system"; content = $systemPrompt },
        @{ role = "user"; content = "Provide a 100x100mm rectangle with auto-dimensions:" }
    )
    temperature = 0.3
} | ConvertTo-Json -Depth 10

Write-Host "System prompt includes auto_dimension: $($systemPrompt.Contains('auto_dimension'))" -ForegroundColor Gray

try {
    $response = Invoke-WebRequest -Uri "https://api.groq.com/openai/v1/chat/completions" `
        -Method Post `
        -Headers @{ "Authorization" = "Bearer $groqApiKey"; "Content-Type" = "application/json" } `
        -Body $payload `
        -TimeoutSec 30 `
        -UseBasicParsing

    $result = $response.Content | ConvertFrom-Json
    $content = $result.choices[0].message.content
    
    Write-Host "Response:" -ForegroundColor Cyan
    Write-Host $content -ForegroundColor Yellow
    
    if ($content.Contains("auto_dimension")) {
        Write-Host "PASS: Response contains auto_dimension" -ForegroundColor Green
    } else {
        Write-Host "FAIL: Response missing auto_dimension" -ForegroundColor Red
    }
} catch {
    Write-Host "ERROR: $($_)" -ForegroundColor Red
}
