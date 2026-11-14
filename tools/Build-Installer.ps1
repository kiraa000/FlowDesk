# Build the FlowDesk installer: publish a fresh self-contained win-x64 folder, then compile
# installer\FlowDesk.iss with Inno Setup 6's ISCC.exe.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1 `
#       -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
#
# Release build into new folders (existing folders are never overwritten or deleted):
#   powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1 `
#       -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
#       -PublishOutput "builds\FlowDesk-1.0.0-task007-<timestamp>" `
#       -InstallerOutput "builds\installer-1.0.0-task007-<timestamp>"
#
# Inno Setup is NOT bundled with FlowDesk and is not downloaded by this script.
param(
    [Parameter(Mandatory = $true)]
    [string]$IsccPath,
    [string]$PublishOutput = "builds\FlowDesk-publish",
    [string]$InstallerOutput = "builds\installer",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "Publish.ps1"
$issFile = Join-Path $root "installer\FlowDesk.iss"

function Resolve-RepoPath {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $root $Path))
}

function Assert-FreshFolder {
    param([string]$Path, [string]$Label, [string]$Option)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label path exists but is not a folder: $Path. Choose a new folder with $Option."
    }
    $existing = @(Get-ChildItem -LiteralPath $Path -Force)
    if ($existing.Count -gt 0) {
        throw "$Label folder is not empty: $Path ($($existing.Count) entries). Existing builds are never overwritten or deleted; pass $Option with a NEW folder."
    }
}

function Test-PathInside {
    param([string]$Child, [string]$Parent)
    $prefix = $Parent.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    return $Child.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

# --- Preflight: fail on a missing ISCC, .iss or unsafe output folder BEFORE publishing. ------------

if (-not (Test-Path -LiteralPath $publishScript -PathType Leaf)) {
    throw "Publish script not found: $publishScript"
}

$isccCandidate = $IsccPath.Trim().Trim('"')
if (-not [System.IO.Path]::IsPathRooted($isccCandidate)) {
    $onPath = Get-Command $isccCandidate -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) {
        $isccCandidate = $onPath.Source
    } else {
        $isccCandidate = Join-Path (Get-Location).Path $isccCandidate
    }
}
if (Test-Path -LiteralPath $isccCandidate -PathType Container) {
    $isccCandidate = Join-Path $isccCandidate "ISCC.exe"
}
$isccFull = [System.IO.Path]::GetFullPath($isccCandidate)
if (-not (Test-Path -LiteralPath $isccFull -PathType Leaf)) {
    throw "ISCC.exe not found at '$isccFull'. Pass -IsccPath with the full path to Inno Setup 6's ISCC.exe (for example 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'). Inno Setup is not installed or downloaded by this repo."
}
if ([System.IO.Path]::GetFileName($isccFull) -ne "ISCC.exe") {
    throw "-IsccPath must point to ISCC.exe, not '$isccFull'."
}

if (-not (Test-Path -LiteralPath $issFile -PathType Leaf)) {
    throw "Installer script not found: $issFile. The .iss is maintained separately; this script does not create or edit it."
}

$publishPath = Resolve-RepoPath $PublishOutput
$installerPath = Resolve-RepoPath $InstallerOutput
if ($publishPath -eq $installerPath) {
    throw "-PublishOutput and -InstallerOutput must be different folders (both resolve to $publishPath)."
}
if ((Test-PathInside $publishPath $installerPath) -or (Test-PathInside $installerPath $publishPath)) {
    throw "-PublishOutput and -InstallerOutput must not be inside each other: $publishPath / $installerPath."
}

Assert-FreshFolder $publishPath "Publish output" "-PublishOutput"
Assert-FreshFolder $installerPath "Installer output" "-InstallerOutput"

# --- Publish ----------------------------------------------------------------------------------------

Write-Host "Publishing FlowDesk ($Configuration, win-x64, self-contained) to $publishPath"
& $publishScript -Output $publishPath -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Publish.ps1 failed with exit code $LASTEXITCODE. The installer was not built."
}

$appExe = Join-Path $publishPath "FlowDesk.App.exe"
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "Publish verification failed: $appExe is missing."
}

$prohibited = @(Get-ChildItem -LiteralPath $publishPath -Recurse -File -Filter "VirtualDesktopAccessor.dll")
if ($prohibited.Count -gt 0) {
    throw "Publish verification failed: VirtualDesktopAccessor.dll must not be shipped (found $($prohibited[0].FullName)). It is third-party and supplied by the user via Settings; no file was deleted."
}

# The expected installer name comes from the published exe's ProductVersion, exactly as the .iss
# AppVersion does. No trailing ".0", "+metadata" or other rewriting.
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($appExe)
$version = $versionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read ProductVersion from $appExe. The installer name cannot be derived; publish an exe with a 3- or 4-part numeric ProductVersion."
}
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Published ProductVersion '$version' is not a 3- or 4-part numeric version. The installer name cannot be derived; set a numeric ProductVersion and publish again."
}

$publishedFiles = @(Get-ChildItem -LiteralPath $publishPath -Recurse -File)
$publishedBytes = ($publishedFiles | Measure-Object -Property Length -Sum).Sum
Write-Host ("Published {0:N0} files, {1:N1} MB; app version {2}." -f $publishedFiles.Count, ($publishedBytes / 1MB), $version)

# --- Compile ----------------------------------------------------------------------------------------

if (-not (Test-Path -LiteralPath $installerPath)) {
    New-Item -ItemType Directory -Force -Path $installerPath | Out-Null
}

$expectedName = "FlowDesk-$version-win-x64-setup.exe"
$expectedPath = Join-Path $installerPath $expectedName

Write-Host "Compiling $issFile"
Write-Host ("  SourceDir={0}" -f $publishPath)
Write-Host ("  OutputDirectory={0}" -f $installerPath)

& $isccFull "/DSourceDir=$publishPath" "/DOutputDirectory=$installerPath" $issFile
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE. The installer was not produced."
}

if (-not (Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
    $produced = @(Get-ChildItem -LiteralPath $installerPath -File | Select-Object -ExpandProperty Name)
    throw "ISCC finished but the expected installer is missing: $expectedPath. Files produced: $($produced -join ', '). Check installer\FlowDesk.iss (OutputBaseFilename) and that it uses the SourceDir and OutputDirectory defines."
}
$setup = Get-Item -LiteralPath $expectedPath
if ($setup.Length -le 0) {
    throw "ISCC produced an empty installer: $expectedPath"
}

Write-Host ("Installer: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
