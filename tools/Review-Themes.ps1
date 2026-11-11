param([ValidateSet('Light','Dark')] [string]$Theme, [string]$EvidenceTag = 'r3')
$ErrorActionPreference = 'Stop'
$smoke = Join-Path $PSScriptRoot 'Smoke-App.ps1'
$shots = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../sol-tasks/shots-001'))
if (-not (Test-Path -LiteralPath $shots)) { throw 'Expected existing shots-001 directory.' }
function TakeShot([string]$page) {
 $path = Join-Path $shots "$page-$($Theme.ToLowerInvariant())-$EvidenceTag.png"
 if (Test-Path -LiteralPath $path) { throw "Preserve existing evidence; choose another screenshot name: $path" }
 & $smoke -Action Shot -OutputPath $path
}
& $smoke -Action ResizeDefault
& $smoke -Action Select -Name Settings
& $smoke -Action Choose -Name Theme
& $smoke -Action Select -Name $Theme
Start-Sleep -Seconds 1
TakeShot 'settings-final'
& $smoke -Action Select -Name Workspaces
TakeShot 'workspaces-final'
& $smoke -Action Click -Name 'Workspace actions'
& $smoke -Action Click -Name Edit
TakeShot 'workspace-details'
& $smoke -Action Select -Name Layouts
& $smoke -Action Select -Name Workspaces
& $smoke -Action Click -Name 'New workspace'
Start-Sleep -Seconds 2
TakeShot 'save-open-windows'
& $smoke -Action Click -Name Cancel
& $smoke -Action Select -Name Layouts
TakeShot 'layouts'
& $smoke -Action Select -Name Focus
TakeShot 'focus'
"PASS: captured all six pages in $Theme app theme."
