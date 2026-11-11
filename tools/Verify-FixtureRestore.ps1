# Requires the two-item workspace created with Smoke-App.ps1 -Action SaveFixtures.
# Refuses to invoke Open if the saved data contains anything except that disposable fixture workspace.
$ErrorActionPreference = 'Stop'
$smoke = Join-Path $PSScriptRoot 'Smoke-App.ps1'
$fixture = Join-Path $PSScriptRoot 'smoke-fixture'
$dataPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FlowDesk/flowdesk.json'
$data = Get-Content -LiteralPath $dataPath -Raw | ConvertFrom-Json
if ($data.Workspaces.Count -ne 1 -or $data.Workspaces[0].Name -ne 'FlowDesk smoke test') {
 throw 'Safety check: expected only the disposable smoke workspace. Nothing opened.'
}
$items = $data.Workspaces[0].Items
$folder = @($items | Where-Object { $_.Kind -eq 'Folder' -and $_.Path -eq $fixture })
$notepad = @($items | Where-Object {
 $_.Kind -eq 'App' -and $_.Name -eq 'Notepad' -and
 [System.IO.Path]::GetFileName($_.App.ExePath) -eq 'Notepad.exe'
})
if ($items.Count -ne 2 -or $folder.Count -ne 1 -or $notepad.Count -ne 1) {
 throw 'Safety check: workspace is not precisely the two disposable fixtures. Nothing opened.'
}
& $smoke -Action CloseFixtures
& $smoke -Action AssertNoFixtures
& $smoke -Action Select -Name Workspaces
& $smoke -Action Click -Name Open
Start-Sleep -Seconds 10
& $smoke -Action AssertFixtures
& $smoke -Action AssertPositions
& $smoke -Action CloseFixtures
& $smoke -Action AssertNoFixtures
& $smoke -Action Select -Name Settings
& $smoke -Action Choose -Name Theme
& $smoke -Action Select -Name Dark
& $smoke -Action Toggle -Name 'Minimize previous workspace windows'
& $smoke -Action CloseApp
& $smoke -Action StartApp
& $smoke -Action AssertWorkspace
& $smoke -Action Select -Name Settings
& $smoke -Action AssertDarkTheme
& $smoke -Action AssertMinimizeOff
& $smoke -Action Toggle -Name 'Minimize previous workspace windows'
& $smoke -Action StartApp
& $smoke -Action AssertSingleton
& $smoke -Action Select -Name Workspaces
& $smoke -Action CloseApp
'PASS: fixture-only restore and exact positions, cleanup, workspace/theme/toggle persistence, single instance.'
