# Publish FlowDesk as a self-contained win-x64 folder (no .NET or Windows App SDK install needed).
# Usage:  powershell -ExecutionPolicy Bypass -File tools\Publish.ps1 [-Output <folder>]
#
# The output folder must be new or empty: an existing build is never overwritten or deleted.
# Trimming, single-file and ReadyToRun stay OFF (WinUI + trimming is not tested here).
param(
    [string]$Output = "builds\FlowDesk-win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\FlowDesk.App\FlowDesk.App.csproj"
$outputPath = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
$outputPath = [System.IO.Path]::GetFullPath($outputPath)

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Project not found: $project"
}

if (Test-Path -LiteralPath $outputPath) {
    if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) {
        throw "Output path exists but is not a folder: $outputPath. Pass -Output with a NEW folder."
    }
    $existing = @(Get-ChildItem -LiteralPath $outputPath -Force)
    if ($existing.Count -gt 0) {
        throw "Output folder already contains $($existing.Count) entries: $outputPath. Existing builds are never overwritten or deleted; pass -Output with a NEW folder."
    }
}

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
    -p:PublishReadyToRun=false `
    -o $outputPath `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$appExe = Join-Path $outputPath "FlowDesk.App.exe"
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "Publish verification failed: $appExe is missing."
}

# VirtualDesktopAccessor.dll is third-party; the user supplies it via Settings. Never ship it.
$prohibited = @(Get-ChildItem -LiteralPath $outputPath -Recurse -File -Filter "VirtualDesktopAccessor.dll")
if ($prohibited.Count -gt 0) {
    throw "Publish verification failed: VirtualDesktopAccessor.dll must not be shipped (found $($prohibited[0].FullName)). It was not deleted; remove it from the publish inputs."
}

$files = @(Get-ChildItem -LiteralPath $outputPath -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($appExe)
$version = $versionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = $versionInfo.FileVersion }
if ([string]::IsNullOrWhiteSpace($version)) { $version = "unknown" }

Write-Host ("Published {0:N0} files, {1:N1} MB; version {2}." -f $files.Count, ($bytes / 1MB), $version)
Write-Host "Run: $appExe"
