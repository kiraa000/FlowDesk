# Publish FlowDesk as a self-contained win-x64 folder (no .NET or Windows App SDK install needed).
# Usage:  powershell -ExecutionPolicy Bypass -File tools\Publish.ps1 [-Output <folder>]
param(
    [string]$Output = "builds\FlowDesk-win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\FlowDesk.App\FlowDesk.App.csproj"
$outputPath = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }

Write-Host "Publishing FlowDesk ($Configuration, win-x64, self-contained) to $outputPath"

# If XAML reports WMC0001 "Unknown type ...Converter" (usually next to WMC1509 "No LocalAssembly"),
# the app assembly itself failed to compile and the XAML pass could not resolve local types. Fix the
# C# errors; the converter declarations in App.xaml are valid and publish succeeds from a clean compile.
& dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $outputPath `
    --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$files = Get-ChildItem -LiteralPath $outputPath -Recurse -File
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host ("Published {0:N0} files, {1:N1} MB." -f $files.Count, ($bytes / 1MB))
Write-Host "Run: $(Join-Path $outputPath 'FlowDesk.App.exe')"
