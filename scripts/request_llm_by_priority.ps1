# Calls ClarificationService.ClarifyMissingDimensionStepsWithDebug from the built add-in
# Produces prompt, raw reply and parsed JSON (if any). Uses AICAD_LLM_PRIORITY order.

$solutionDir = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $solutionDir "bin\Debug\net48"
$dll = Join-Path $bin "AI-CAD-December.dll"
$nw = Join-Path $bin "Newtonsoft.Json.dll"
if (-not (Test-Path $dll)) { Write-Error "DLL not found: $dll. Build the solution first."; exit 1 }
if (-not (Test-Path $nw)) { Write-Error "Newtonsoft not found in output folder: $nw"; exit 1 }

Write-Host "Loading assemblies..."
[Reflection.Assembly]::LoadFrom($nw) | Out-Null
[Reflection.Assembly]::LoadFrom($dll) | Out-Null

# Build a sample missing-dimensions JArray using the exact Newtonsoft assembly loaded above
$sample = '[ { "op": "dimension", "cx": null, "cy": null, "w": null, "h": null } ]'
$loadedNewtonsoft = ([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Newtonsoft.Json' })[0]
if (-not $loadedNewtonsoft) { Write-Error "Newtonsoft assembly not loaded"; exit 1 }
$jarrayType = $loadedNewtonsoft.GetType('Newtonsoft.Json.Linq.JArray')
if (-not $jarrayType) { Write-Error "Unable to get JArray type from loaded Newtonsoft"; exit 1 }
$parseMethod = $jarrayType.GetMethod('Parse', [string])
if (-not $parseMethod) { Write-Error "JArray.Parse not found"; exit 1 }
$jarr = $parseMethod.Invoke($null, @($sample))

$svcType = [AICAD.Services.ClarificationService]

# Prefer calling ClarifySingleStep (accepts a JObject) to avoid JArray type mismatches across loaded assemblies.
$singleSample = '{ "op": "dimension", "cx": null, "cy": null, "w": null, "h": null }'
$jobjType = $loadedNewtonsoft.GetType('Newtonsoft.Json.Linq.JObject')
$jobjParse = $jobjType.GetMethod('Parse', [string])
$jobj = $jobjParse.Invoke($null, @($singleSample))

$singleMethod = $svcType.GetMethod('ClarifySingleStep',[Type[]]@($jobjType, [object]))
if ($singleMethod) {
    Write-Host "Invoking ClarificationService.ClarifySingleStep with provider priority: $env:AICAD_LLM_PRIORITY"
    try {
        $ret = $singleMethod.Invoke($null, @($jobj, $null))
        if ($ret -ne $null) { Write-Host "--- Returned token ---"; Write-Host $ret.ToString() } else { Write-Host "No token returned" }
    } catch {
        Write-Error "ClarifySingleStep invocation failed: $($_.Exception.Message)"
        if ($_.Exception.InnerException) { Write-Error "Inner: $($_.Exception.InnerException.Message)" }
    }
} else {
    Write-Host "ClarifySingleStep overload not found, falling back to ClarifyMissingDimensionStepsWithDebug"
    $method = $svcType.GetMethod('ClarifyMissingDimensionStepsWithDebug',[Type[]]@($jarrayType))
    if (-not $method) { Write-Error "Method ClarifyMissingDimensionStepsWithDebug not found"; exit 1 }
    Write-Host "Invoking ClarificationService.ClarifyMissingDimensionStepsWithDebug with provider priority: $env:AICAD_LLM_PRIORITY"
    try {
        $res = $method.Invoke($null, @($jarr))
        if ($null -eq $res) { Write-Host "Result is null"; exit 0 }
        $prompt = $res.Prompt
        $raw = $res.RawReply
        $parsed = $res.Parsed
        Write-Host "--- Prompt sent ---"
        Write-Host $prompt
        Write-Host "--- Raw reply (truncated 200 chars) ---"
        if ($raw) { Write-Host $raw.Substring(0,[Math]::Min(200,$raw.Length)) } else { Write-Host "(no raw reply)" }
        Write-Host "--- Parsed (if any) ---"
        if ($parsed) { Write-Host $parsed.ToString() } else { Write-Host "(no parsed JSON)" }
    } catch {
        Write-Error "Invocation failed: $($_.Exception.Message)"
        if ($_.Exception.InnerException) { Write-Error "Inner: $($_.Exception.InnerException.Message)" }
        exit 1
    }
    Write-Host "Done."
}
Write-Host "Done."