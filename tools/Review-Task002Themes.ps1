param(
 [Parameter(Mandatory)][ValidateSet('Light','Dark')][string]$Theme,
 [string]$EvidenceTag = 'task002',
 [switch]$LayoutsOnly
)
$ErrorActionPreference = 'Stop'
$smoke = Join-Path $PSScriptRoot 'Smoke-App.ps1'
$verify = Join-Path $PSScriptRoot 'Verify-Task002.ps1'
$shots = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/screenshots'))
New-Item -ItemType Directory -Force -Path $shots | Out-Null

function Invoke-Smoke {
    param([Parameter(Mandatory)][string]$Action, [string]$Name, [string]$Value, [string]$OutputPath)
    $arguments = @{ Action = $Action }
    if ($PSBoundParameters.ContainsKey('Name')) { $arguments.Name = $Name }
    if ($PSBoundParameters.ContainsKey('Value')) { $arguments.Value = $Value }
    if ($PSBoundParameters.ContainsKey('OutputPath')) { $arguments.OutputPath = $OutputPath }
    & $smoke @arguments
}

function Take-Shot([string]$Name) {
    $path = Join-Path $shots "$Name-$($Theme.ToLowerInvariant())-$EvidenceTag.png"
    if (Test-Path -LiteralPath $path) {
        throw "Preserve existing evidence; choose another screenshot name: $path"
    }
    Invoke-Smoke -Action Shot -OutputPath $path
}

"Reviewing $Theme theme with evidence tag '$EvidenceTag'."

# Preconditions: the single known fixture workspace and both disposable windows are the evidence source.
$dataPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FlowDesk/flowdesk.json'
$data = Get-Content -LiteralPath $dataPath -Raw | ConvertFrom-Json
$workspaces = @($data.Workspaces)
$fixture = @($workspaces | Where-Object Name -eq 'FlowDesk smoke test')
if ($workspaces.Count -ne 1 -or $fixture.Count -ne 1) {
    throw 'Expected only the FlowDesk smoke test workspace; refusing to capture wrong Details evidence.'
}
Invoke-Smoke -Action AssertFixtures

# Theme through the real Settings UI, exercising the theme resources.
Invoke-Smoke -Action ResizeDefault
Invoke-Smoke -Action Select -Name Settings
Invoke-Smoke -Action Choose -Name Theme
Invoke-Smoke -Action Select -Name $Theme
Start-Sleep -Seconds 1

# Layouts refreshes live windows on navigation; the fixture windows are already active.
Invoke-Smoke -Action Select -Name Layouts
Start-Sleep -Seconds 2
& $verify -Action DismissStatus
& $verify -Action AssertMonitor
Take-Shot 'layouts'
if ($LayoutsOnly) { "PASS: captured Layouts in $Theme with a selected monitor."; return }

# Focus idle: top and bottom, because the page is taller than the default window.
Invoke-Smoke -Action Select -Name Focus
& $verify -Action DismissStatus
Take-Shot 'focus-idle-top'
Invoke-Smoke -Action ScrollBottom
Start-Sleep -Milliseconds 500
Take-Shot 'focus-idle-bottom'

# Reset scroll to the top, then start only a safety-asserted Normal session through the helper.
Invoke-Smoke -Action Select -Name Workspaces
Invoke-Smoke -Action Select -Name Focus
& $verify -Action Normal
Start-Sleep -Seconds 1
& $verify -Action DismissStatus
Take-Shot 'focus-running'
& $verify -Action EndSession
Start-Sleep -Seconds 1
& $verify -Action OpenNotepad

# Save open windows in a compact window, then restore the default size.
Invoke-Smoke -Action Select -Name Workspaces
Invoke-Smoke -Action Click -Name 'New workspace'
Start-Sleep -Seconds 2
Take-Shot 'save-open-windows'
Invoke-Smoke -Action ResizeNarrow
Take-Shot 'save-open-windows-compact'
Invoke-Smoke -Action Click -Name Cancel
Invoke-Smoke -Action ResizeDefault

# Fixture workspace details via Workspace actions > Edit.
Invoke-Smoke -Action Select -Name Workspaces
Invoke-Smoke -Action Click -Name 'Workspace actions'
Invoke-Smoke -Action Click -Name Edit
Start-Sleep -Seconds 1
Take-Shot 'details'

"PASS: captured seven views in $Theme app theme with tag '$EvidenceTag'."
