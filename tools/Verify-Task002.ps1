param([ValidateSet('PickNotepad','Normal','Strict','AssertClosed','AssertAllowed','Allow','AssertStrict','HideReopen','EndSession','Layout',
 'StartDiagnostics','StartShortNormal','StartShortStrict','AllowLogged','AssertLoggedStrict','AssertCompletion','ClearNotifications','Snapshot','OpenNotepad','DismissStatus','PauseResume','AssertMonitor')][string]$Action)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
if ($Action -eq 'StartDiagnostics') {
    if (Get-Process FlowDesk.App -ErrorAction SilentlyContinue) { throw 'Close FlowDesk normally before starting diagnostic mode.' }
    $old = $env:FLOWDESK_TASK002_DIAGNOSTICS
    try {
        $env:FLOWDESK_TASK002_DIAGNOSTICS = '1'
        Start-Process -FilePath (Join-Path $PSScriptRoot '../src/FlowDesk.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/FlowDesk.App.exe')
    } finally { $env:FLOWDESK_TASK002_DIAGNOSTICS = $old }
    Start-Sleep -Seconds 5
    'PASS: launched opt-in Debug diagnostics; no test session started.'; exit
}
$process = Get-Process FlowDesk.App -ErrorAction Stop
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
function Diagnostic([string]$command, [string]$argument = '') {
    $folder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FlowDesk/task002-debug'
    if (-not (Test-Path -LiteralPath (Split-Path $folder))) { throw 'FlowDesk data folder is missing.' }
    [void][System.IO.Directory]::CreateDirectory($folder)
    $id = [Guid]::NewGuid().ToString('N')
    $request = @{ Id = $id; Action = $command; Argument = $argument } | ConvertTo-Json
    [System.IO.File]::WriteAllText((Join-Path $folder 'request.json'), $request)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $responseFile = Join-Path $folder 'response.json'
        if (-not (Test-Path -LiteralPath $responseFile)) { continue }
        $response = Get-Content -LiteralPath $responseFile -Raw | ConvertFrom-Json
        if ($response.Id -ne $id) { continue }
        # Preserve each diagnostic payload/activation result for review; response.json itself is a live mailbox.
        $evidence = Join-Path $folder 'evidence'
        [void][System.IO.Directory]::CreateDirectory($evidence)
        [System.IO.File]::WriteAllText((Join-Path $evidence "$id.json"), ($response | ConvertTo-Json -Depth 12))
        if ($response.Error) { throw $response.Error }
        return $response
    }
    throw 'No diagnostic response. Start FlowDesk with StartDiagnostics first.'
}
if ($Action -in @('StartShortNormal','StartShortStrict')) {
    $result = Diagnostic $Action
    if (-not $result.Running -or $result.Remaining -gt 60) { throw 'One-minute fixture session was not started.' }
    "PASS: started one-minute $($result.Strictness) fixture session; only notepad is blocked."; exit
}
if ($Action -eq 'Snapshot') {
    $result = Diagnostic 'Snapshot'
    "PASS: diagnostic snapshot Running=$($result.Running), Strictness=$($result.Strictness), notification count=$($result.Notifications.Count)."; exit
}
if ($Action -eq 'ClearNotifications') {
    [void](Diagnostic 'ClearNotifications'); 'PASS: cleared only FlowDesk notification history and diagnostic evidence.'; exit
}
if ($Action -eq 'AllowLogged') {
    $result = Diagnostic 'Snapshot'
    if (-not $result.Running -or $result.Strictness -ne 'Normal') { throw 'Expected active Normal session.' }
    $entry = @($result.Notifications | Where-Object { $_.Payload -like '*Allow 5 minutes*' })[-1]
    if (-not $entry -or $entry.Id -eq 0) { throw 'No accepted Normal notification with allowance action.' }
    [xml]$xml = $entry.Payload
    $argument = $xml.toast.actions.action.arguments
    if ($argument -notmatch '^allow=[a-f0-9]{32}$') { throw 'Unexpected activation argument.' }
    $inHistory = $entry.Id -in $result.PlatformNotificationIds
    [void](Diagnostic 'Activate' $argument)
    "PASS: AppNotification Show returned nonzero ID, platform history present=$inHistory; replayed its exact logged allowance argument through the real activation handler (not a Windows button click)."; exit
}
if ($Action -eq 'AssertLoggedStrict') {
    $result = Diagnostic 'Snapshot'
    if (-not $result.Running -or $result.Strictness -ne 'Strict') { throw 'Expected active Strict session.' }
    $entry = @($result.Notifications | Where-Object { $_.Payload -like '*Notepad was closed*' })[-1]
    if (-not $entry -or $entry.Id -eq 0 -or $entry.Payload -like '*<action *') { throw 'Strict notification missing or exposes an action.' }
    'PASS: Strict notification submitted with nonzero ID and no allowance action.'; exit
}
if ($Action -eq 'AssertCompletion') {
    $deadline = [DateTime]::UtcNow.AddSeconds(75)
    do {
        $result = Diagnostic 'Snapshot'
        if (-not $result.Running) { break }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    $entry = @($result.Notifications | Where-Object { $_.Payload -like '*Focus session complete*' })[-1]
    if ($result.Running -or -not $entry -or $entry.Id -eq 0) { throw 'Timed completion notification not received.' }
    $inHistory = $entry.Id -in $result.PlatformNotificationIds
    "PASS: session ended naturally; Focus session complete notification submitted, platform history present=$inHistory."; exit
}
function Find([string]$name, $scope = $window) {
    $result = $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    if (-not $result) { throw "UI control not found: $name" }; return $result
}
function Invoke([string]$name) {
    (Find $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 600
}
function SelectRadio([string]$name) {
    $elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    $radio = @($elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::RadioButton })[0]
    if (-not $radio) { throw "Radio button not found: $name" }
    $radio.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function AssertSafeSettings {
    $file = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FlowDesk/flowdesk.json'
    $focus = (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).Focus
    if ($focus.BlockAllExceptAllowed -or $focus.Blocked.Count -ne 1 -or $focus.Blocked[0].ExeName -ne 'notepad' -or $focus.Allowed.Count) {
        throw 'Refusing focus test: settings must block only notepad, with no allowed apps and block-all OFF.'
    }
}
if ($Action -eq 'DismissStatus') {
    $bar = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'AppStatusBar'))
    # Never select a Close by its order in the window: caption controls can be duplicated in UIA.
    if ($bar) {
        $button = $bar.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Close'))
        if ($button) { $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    }
    Start-Sleep -Milliseconds 400
    'PASS: dismissed in-app status if present (never caption Close).'; exit
}
if ($Action -eq 'AssertMonitor') {
    $group = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MonitorSelector'))
    if (-not $group) { throw 'Monitor radio group not found.' }
    $radios = $group.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton))
    $selected = @($radios | Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected })
    if ($radios.Count -lt 2 -or $selected.Count -ne 1) { throw 'Expected exactly one selected monitor on this multi-monitor desktop.' }
    'PASS: exactly one monitor is visibly selected.'; exit
}
if ($Action -eq 'PauseResume') {
    Invoke 'Pause'
    $first = Diagnostic 'Snapshot'
    Start-Sleep -Seconds 2
    $second = Diagnostic 'Snapshot'
    if (-not $first.Paused -or -not $second.Paused -or [Math]::Abs($second.Remaining - $first.Remaining) -gt 0.5) { throw 'Pause did not freeze remaining time.' }
    Invoke 'Resume'
    $resumed = Diagnostic 'Snapshot'
    if ($resumed.Paused -or -not $resumed.Running) { throw 'Session did not resume.' }
    'PASS: Pause freezes remaining time; Resume restarts the session.'; exit
}
if ($Action -eq 'OpenNotepad') {
    $existing = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'note.txt - Notepad'))
    if ($existing.Count) { throw 'Disposable Notepad is already open; refusing to duplicate it.' }
    Start-Process notepad.exe -ArgumentList (Join-Path $PSScriptRoot 'smoke-fixture/note.txt')
    Start-Sleep -Seconds 2
    'Opened only disposable note.txt Notepad.'; exit
}
if ($Action -eq 'PickNotepad') {
    $dialog = Find 'Pick a running app'
    $node = Find 'notepad.exe' $dialog
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($node -and $node.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) { $node = $walker.GetParent($node) }
    if (-not $node) { throw 'Notepad list item not found.' }
    $node.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke 'Add app'; 'PASS: selected only disposable Notepad for the blocked list.'; exit
}
if ($Action -in @('Normal','Strict')) {
    AssertSafeSettings
    SelectRadio $Action
    Start-Sleep -Seconds 1
    Invoke 'Start focus'
    'PASS: started ' + $Action + ' session with only notepad blocked.'; exit
}
if ($Action -eq 'AssertClosed') {
    Start-Sleep -Seconds 3
    $notes = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'note.txt - Notepad'))
    if ($notes.Count) { throw 'Disposable Notepad was not closed.' }
    'PASS: disposable Notepad window closed.'; exit
}
if ($Action -eq 'AssertAllowed') {
    Start-Process notepad.exe -ArgumentList (Join-Path $PSScriptRoot 'smoke-fixture/note.txt')
    Start-Sleep -Seconds 4
    $notes = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'note.txt - Notepad'))
    if ($notes.Count -ne 1) { throw 'Allowed Notepad did not stay open.' }
    'PASS: temporarily allowed Notepad stays open across multiple watcher polls.'; exit
}
if ($Action -eq 'Allow') {
    # Inspect only the notification button requested by this test, never unrelated notification text.
    $button = Find 'Allow 5 minutes' $root
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    'PASS: invoked Windows notification Allow 5 minutes.'; exit
}
if ($Action -eq 'AssertStrict') {
    $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Pause'))
    if ($buttons.Count) { throw 'Strict session exposes Pause.' }
    $allow = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Allow 5 minutes'))
    if ($allow.Count) { throw 'Strict test found a temporary allowance button.' }
    'PASS: strict session has no pause or temporary allowance button.'; exit
}
if ($Action -eq 'HideReopen') {
    [void]$process.CloseMainWindow(); Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited -or $process.MainWindowHandle -ne 0) { throw 'Focus close did not hide the app.' }
    Start-Process -FilePath (Join-Path $PSScriptRoot '../src/FlowDesk.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/FlowDesk.App.exe')
    Start-Sleep -Seconds 3; $process.Refresh()
    if ($process.MainWindowHandle -eq 0) { throw 'Second launch did not reopen the hidden window.' }
    'PASS: close hid the running app; second launch reopened it.'; exit
}
if ($Action -eq 'EndSession') {
    Invoke 'End session'
    $dialogs = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'End strict focus?'))
    if ($dialogs.Count) {
        $dialog = $dialogs[0]
        (Find 'End session' $dialog).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    Start-Sleep -Seconds 1; 'PASS: ended focus session (confirmed when Strict).'; exit
}
if ($Action -eq 'Layout') {
    # Compare saved/live outer rects via the existing fixture assertion; also verify non-overlap and 50/50 widths.
    $shell = New-Object -ComObject Shell.Application
    $fixture = Join-Path $PSScriptRoot 'smoke-fixture'
    $folders = @($shell.Windows() | Where-Object { try { $_.Document.Folder.Self.Path -eq $fixture } catch { $false } })
    if ($folders.Count -ne 1) { throw 'Expected exactly one fixture Explorer window.' }
    $file = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FlowDesk/flowdesk.json'
    $workspace = @((Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).Workspaces | Where-Object Name -eq 'FlowDesk smoke test')
    if ($workspace.Count -ne 1 -or $workspace[0].Items.Count -ne 2) { throw 'Unexpected fixture workspace.' }
    $a = $workspace[0].Items[0].Window.Rect; $b = $workspace[0].Items[1].Window.Rect
    if ([Math]::Abs($a.Width - $b.Width) -gt 32 -or [Math]::Abs($a.Y - $b.Y) -gt 16 -or [Math]::Abs($a.Height - $b.Height) -gt 16) {
        throw 'Two columns did not produce equal-width aligned windows.'
    }
    if ([Math]::Abs($a.X - $b.X) -lt ($a.Width - 32)) { throw 'Two-column windows overlap.' }
    & (Join-Path $PSScriptRoot 'Smoke-App.ps1') -Action AssertPositions
    'PASS: saved and live fixture positions match an aligned two-column layout.'
}
