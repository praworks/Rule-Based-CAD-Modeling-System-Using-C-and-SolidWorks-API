# Elevated build script: runs msbuild or dotnet build and writes a timestamped log
$root = "D:\SolidWorks API\8. SolidWorks Taskpane Text To CAD"
$meta = Join-Path $root "elevated_build_last.txt"
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$log = Join-Path $root "elevated_build_log_$timestamp.txt"
"Starting elevated build at: $(Get-Date)" | Out-File -FilePath $log -Encoding utf8
"Solution: $root\SolidWorks.TaskpaneCalculator.sln" | Out-File -FilePath $log -Append -Encoding utf8
$ms = Get-Command msbuild -ErrorAction SilentlyContinue
if ($ms) {
    "Found msbuild at: $($ms.Path)" | Out-File -FilePath $log -Append -Encoding utf8
    & msbuild (Join-Path $root 'SolidWorks.TaskpaneCalculator.sln') /p:Configuration=Debug /m *>&1 | Out-File -FilePath $log -Append -Encoding utf8
} else {
    $d = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($d) {
        "Found dotnet at: $($d.Path)" | Out-File -FilePath $log -Append -Encoding utf8
        dotnet build (Join-Path $root 'SolidWorks.TaskpaneCalculator.sln') -c Debug *>&1 | Out-File -FilePath $log -Append -Encoding utf8
    } else {
        "MSBuild and dotnet not found in PATH" | Out-File -FilePath $log -Append -Encoding utf8
    }
}
"Finished at: $(Get-Date)" | Out-File -FilePath $log -Append -Encoding utf8
# write pointer file and done marker
$log | Out-File -FilePath $meta -Encoding utf8
"DONE" | Out-File -FilePath ($log + '.done') -Encoding utf8
