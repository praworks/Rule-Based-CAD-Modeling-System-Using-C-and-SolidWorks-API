Set-StrictMode -Version Latest

Set-Location -LiteralPath 'D:\SolidWorks Project\Rule-Based-CAD-Modeling-System-Using-C-and-SolidWorks-API'

Write-Host "Cleaning obj and bin folders..."
if (Test-Path obj) { Remove-Item -Recurse -Force obj }
if (Test-Path build_obj) { Remove-Item -Recurse -Force build_obj }
if (Test-Path bin) { Remove-Item -Recurse -Force bin }
if (Test-Path ".vs") { Remove-Item -Recurse -Force ".vs" }

Write-Host "Restoring NuGet packages..."

try {
    dotnet restore
} catch {
    Write-Error "dotnet restore failed: $_"
    Read-Host -Prompt 'Press Enter to close'
    exit 1
}

Write-Host "Running MSBuild..."

$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild 'AI-CAD-December.sln' /t:Rebuild /p:Configuration=Debug /p:Platform="Any CPU"

Write-Host "Build finished. Press Enter to close."
Read-Host -Prompt 'Press Enter to close'
