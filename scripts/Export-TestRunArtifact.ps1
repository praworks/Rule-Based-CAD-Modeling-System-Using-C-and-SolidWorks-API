[CmdletBinding()]
param(
    [string]$TestId,
    [string]$TraceId,
    [string]$RunId,
    [string]$TranscriptPath,
    [string]$LoggingDir,
    [string]$OutputDir,
    [string]$SolidWorksProcessName = "SLDWORKS",
    [switch]$SkipScreenshot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return Split-Path -Parent $PSScriptRoot
}

function Resolve-DefaultLoggingDir {
    $docs = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    if ([string]::IsNullOrWhiteSpace($docs)) {
        return (Join-Path (Get-RepoRoot) "Logging")
    }

    return (Join-Path $docs "AICAD\Logging")
}

function Resolve-OutputDir([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return (Join-Path (Get-RepoRoot) "TestArtifacts")
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return (Join-Path (Get-RepoRoot) $PathValue)
}

function Normalize-Text([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $value = $Text.ToLowerInvariant()
    $value = [regex]::Replace($value, "\r?\n", " ")
    $value = [regex]::Replace($value, "[^\w\s\.]", " ")
    $value = [regex]::Replace($value, "\s+", " ")
    return $value.Trim(" ", ".")
}

function Get-TestCaseMap {
    $repoRoot = Get-RepoRoot
    $testCaseRoot = Join-Path $repoRoot "TestCases\NL_TextToCAD"
    $map = @{}

    if (-not (Test-Path -LiteralPath $testCaseRoot)) {
        return $map
    }

    Get-ChildItem -LiteralPath $testCaseRoot -Filter "*.txt" -File | Sort-Object Name | ForEach-Object {
        if ($_.Name -ieq "00_INDEX.txt") {
            return
        }

        Get-Content -LiteralPath $_.FullName | ForEach-Object {
            $rawLine = $_
            if ($null -eq $rawLine) {
                $rawLine = ""
            }

            $line = $rawLine.Trim()
            if ($line -notmatch '^(TC\d+)\s+(.+)$') {
                return
            }

            $id = $matches[1].Trim()
            $prompt = $matches[2].Trim()
            $map[$id.ToUpperInvariant()] = [pscustomobject]@{
                Id = $id.ToUpperInvariant()
                Prompt = $prompt
                NormalizedPrompt = Normalize-Text $prompt
                SourceFile = $_.PSPath
            }
        }
    }

    return $map
}

function Get-TranscriptContent([string]$PathValue) {
    return [System.IO.File]::ReadAllText($PathValue)
}

function Get-UserRequestFromTranscript([string]$TranscriptText) {
    if ([string]::IsNullOrWhiteSpace($TranscriptText)) {
        return $null
    }

    $requestPattern = '(?ms)USER:\s*.*?USER REQUEST:\s*(?<request>.+?)(?:\r?\n\r?\nReturn only|\r?\nReturn only|^ASSISTANT:|\z)'
    $requestMatch = [regex]::Match($TranscriptText, $requestPattern)
    if ($requestMatch.Success) {
        return $requestMatch.Groups["request"].Value.Trim()
    }

    $userPattern = '(?ms)^USER:\s*(?<user>.+?)(?:^ASSISTANT:|\z)'
    $userMatch = [regex]::Match($TranscriptText, $userPattern)
    if ($userMatch.Success) {
        return $userMatch.Groups["user"].Value.Trim()
    }

    return $null
}

function Get-TranscriptTimestamp([string]$TranscriptText, [System.IO.FileInfo]$FileInfo) {
    if (-not [string]::IsNullOrWhiteSpace($TranscriptText)) {
        $match = [regex]::Match($TranscriptText, '\[(?<ts>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?)\]')
        if ($match.Success) {
            try {
                return [datetime]::Parse($match.Groups["ts"].Value, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal)
            }
            catch { }
        }
    }

    if ($FileInfo -ne $null) {
        return $FileInfo.LastWriteTime
    }

    return [datetime]::Now
}

function Resolve-TranscriptFile {
    param(
        [string]$TranscriptPathValue,
        [string]$LoggingDirValue,
        [string]$TraceIdValue,
        [string]$RunIdValue,
        [string]$RequestedTestId,
        [hashtable]$TestCaseMap
    )

    if (-not [string]::IsNullOrWhiteSpace($TranscriptPathValue)) {
        $resolved = Resolve-Path -LiteralPath $TranscriptPathValue -ErrorAction Stop
        return Get-Item -LiteralPath $resolved.Path
    }

    if (-not (Test-Path -LiteralPath $LoggingDirValue)) {
        throw "Logging directory not found: $LoggingDirValue"
    }

    $files = Get-ChildItem -LiteralPath $LoggingDirValue -Filter "llm_chat_*.txt" -File | Sort-Object LastWriteTime -Descending
    if (-not $files) {
        throw "No llm_chat transcript files found in $LoggingDirValue"
    }

    $traceKey = if (-not [string]::IsNullOrWhiteSpace($TraceIdValue)) { $TraceIdValue } elseif (-not [string]::IsNullOrWhiteSpace($RunIdValue)) { $RunIdValue } else { $null }
    if (-not [string]::IsNullOrWhiteSpace($traceKey)) {
        $traceMatch = $files | Where-Object { $_.BaseName -like "*$traceKey*" } | Select-Object -First 1
        if ($traceMatch -ne $null) {
            return $traceMatch
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedTestId) -and $TestCaseMap.ContainsKey($RequestedTestId)) {
        $normalizedPrompt = $TestCaseMap[$RequestedTestId].NormalizedPrompt
        foreach ($file in $files) {
            $content = Get-TranscriptContent $file.FullName
            $request = Get-UserRequestFromTranscript $content
            if ((Normalize-Text $request) -eq $normalizedPrompt) {
                return $file
            }
        }
    }

    return $files[0]
}

function Resolve-TestIdFromTranscript {
    param(
        [string]$RequestedTestId,
        [string]$TranscriptText,
        [hashtable]$TestCaseMap
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedTestId)) {
        return $RequestedTestId.ToUpperInvariant()
    }

    $request = Get-UserRequestFromTranscript $TranscriptText
    $normalizedRequest = Normalize-Text $request
    if ([string]::IsNullOrWhiteSpace($normalizedRequest)) {
        return "RUN"
    }

    foreach ($entry in $TestCaseMap.GetEnumerator()) {
        if ($entry.Value.NormalizedPrompt -eq $normalizedRequest) {
            return $entry.Value.Id
        }
    }

    return "RUN"
}

function Save-TranscriptArtifact {
    param(
        [string]$DestinationPath,
        [string]$ResolvedTestId,
        [datetime]$Timestamp,
        [System.IO.FileInfo]$SourceFile,
        [string]$TranscriptText
    )

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.AppendLine("TestId: $ResolvedTestId")
    [void]$builder.AppendLine("ExportedAt: $([datetime]::Now.ToString("yyyy-MM-dd HH:mm:ss"))")
    [void]$builder.AppendLine("TranscriptTimestamp: $($Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))")
    [void]$builder.AppendLine("SourceTranscript: $($SourceFile.FullName)")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine($TranscriptText)

    [System.IO.File]::WriteAllText($DestinationPath, $builder.ToString(), [System.Text.Encoding]::UTF8)
}

function Ensure-WindowCaptureTypes {
    if ("Win32Capture.NativeMethods" -as [type]) {
        return
    }

    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

namespace Win32Capture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    }
}
"@
}

function Save-SolidWorksScreenshot {
    param(
        [string]$DestinationPath,
        [string]$ProcessName
    )

    Add-Type -AssemblyName System.Drawing
    Ensure-WindowCaptureTypes

    $process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        Sort-Object StartTime -Descending |
        Select-Object -First 1

    if ($process -eq $null) {
        throw "SolidWorks process '$ProcessName' with a visible main window was not found."
    }

    $rect = New-Object Win32Capture.RECT
    if (-not [Win32Capture.NativeMethods]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
        throw "Failed to get SolidWorks main window bounds."
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "SolidWorks main window bounds are invalid."
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedLoggingDir = if ([string]::IsNullOrWhiteSpace($LoggingDir)) { Resolve-DefaultLoggingDir } else { $LoggingDir }
$resolvedOutputDir = Resolve-OutputDir $OutputDir
[System.IO.Directory]::CreateDirectory($resolvedOutputDir) | Out-Null

$testCaseMap = Get-TestCaseMap
$requestedTestId = if ([string]::IsNullOrWhiteSpace($TestId)) { $null } else { $TestId.ToUpperInvariant() }
$transcriptFile = Resolve-TranscriptFile -TranscriptPathValue $TranscriptPath -LoggingDirValue $resolvedLoggingDir -TraceIdValue $TraceId -RunIdValue $RunId -RequestedTestId $requestedTestId -TestCaseMap $testCaseMap
$transcriptText = Get-TranscriptContent $transcriptFile.FullName
$resolvedTestId = Resolve-TestIdFromTranscript -RequestedTestId $requestedTestId -TranscriptText $transcriptText -TestCaseMap $testCaseMap
$timestamp = Get-TranscriptTimestamp -TranscriptText $transcriptText -FileInfo $transcriptFile
$baseName = "{0}_{1}" -f $resolvedTestId, $timestamp.ToString("yyyyMMdd_HHmmss")
$textPath = Join-Path $resolvedOutputDir ($baseName + ".txt")
$imagePath = Join-Path $resolvedOutputDir ($baseName + ".png")

Save-TranscriptArtifact -DestinationPath $textPath -ResolvedTestId $resolvedTestId -Timestamp $timestamp -SourceFile $transcriptFile -TranscriptText $transcriptText

$screenshotSaved = $false
$screenshotError = $null
if (-not $SkipScreenshot) {
    try {
        Save-SolidWorksScreenshot -DestinationPath $imagePath -ProcessName $SolidWorksProcessName
        $screenshotSaved = $true
    }
    catch {
        $screenshotError = $_.Exception.Message
    }
}

[pscustomobject]@{
    TestId = $resolvedTestId
    TranscriptSource = $transcriptFile.FullName
    TranscriptArtifact = $textPath
    ScreenshotArtifact = if ($screenshotSaved) { $imagePath } else { $null }
    ScreenshotSaved = $screenshotSaved
    ScreenshotError = $screenshotError
}
