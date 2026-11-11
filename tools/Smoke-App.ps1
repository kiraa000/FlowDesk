param(
 [ValidateSet('Inspect','Click','SetText','Select','Shot','Toggle','Choose','Invoke','Expand',
     'SaveFixtures','StartFixtures','CloseFixtures','CloseApp','StartApp','AssertFixtures','AssertWorkspace',
     'AssertPositions','AssertLightTheme','AssertDarkTheme','AssertMinimizeOff','AssertSingleton',
     'CloseDialog','AssertNoFixtures','ResizeNarrow','ResizeDefault','ScrollBottom')]
 [string]$Action = 'Inspect', [string]$Name, [string]$Value, [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
# App-only UI Automation harness. Never invoke Open against a personal workspace.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SmokeNative {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
 public struct Rect { public int Left, Top, Right, Bottom; }
}
'@
$fixture = Join-Path $PSScriptRoot 'smoke-fixture'
if ($Action -eq 'StartFixtures') {
 Start-Process notepad.exe -ArgumentList (Join-Path $fixture 'note.txt')
 Start-Process explorer.exe -ArgumentList $fixture
 Start-Sleep -Seconds 3
 'Disposable Notepad and Explorer fixtures opened.'; exit
}
if ($Action -in @('CloseFixtures','AssertFixtures','AssertPositions','AssertNoFixtures')) {
 $desktop = [System.Windows.Automation.AutomationElement]::RootElement
 $children = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children,
     [System.Windows.Automation.Condition]::TrueCondition)
 $notepads = @($children | Where-Object { $_.Current.Name -eq 'note.txt - Notepad' })
 $shell = New-Object -ComObject Shell.Application
 $folders = @($shell.Windows() | Where-Object {
     try { $_.Document.Folder.Self.Path -eq $fixture } catch { $false }
 })
 if ($Action -eq 'AssertNoFixtures') {
  if ($notepads.Count -or $folders.Count) { throw 'Disposable fixture windows remain open.' }
  'PASS: both disposable fixture windows are closed.'; exit
 }
 if ($Action -eq 'AssertPositions') {
  if ($notepads.Count -ne 1 -or $folders.Count -ne 1) { throw 'Expected both fixtures for position verification.' }
  $dataPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FlowDesk/flowdesk.json'
  $data = Get-Content -LiteralPath $dataPath -Raw | ConvertFrom-Json
  $workspace = @($data.Workspaces | Where-Object Name -eq 'FlowDesk smoke test')
  if ($workspace.Count -ne 1 -or $workspace[0].Items.Count -ne 2) { throw 'Expected one two-item fixture workspace.' }
  foreach ($item in $workspace[0].Items) {
   $handle = if ($item.Name -eq 'Notepad') { $notepads[0].Current.NativeWindowHandle } else { $folders[0].HWND }
   $r = New-Object SmokeNative+Rect
   [void][SmokeNative]::GetWindowRect([IntPtr]$handle, [ref]$r)
   $saved = $item.Window.Rect
   $delta = @([Math]::Abs($r.Left-$saved.X), [Math]::Abs($r.Top-$saved.Y),
       [Math]::Abs(($r.Right-$r.Left)-$saved.Width), [Math]::Abs(($r.Bottom-$r.Top)-$saved.Height))
   if (@($delta | Where-Object { $_ -gt 16 }).Count) { throw "Position mismatch for $($item.Name): $delta" }
   "PASS position $($item.Name): pixel deltas $($delta -join ',') (tolerance 16)."
  }
  exit
 }
 if ($Action -eq 'AssertFixtures') {
  if ($notepads.Count -ne 1 -or $folders.Count -ne 1) {
   throw "Expected exactly one of each fixture: Notepad=$($notepads.Count), Explorer=$($folders.Count)"
  }
  'PASS: Notepad note.txt and the exact smoke-fixture Explorer folder are open.'; exit
 }
 foreach ($n in $notepads) { $n.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
 foreach ($f in $folders) { $f.Quit() }
 Start-Sleep -Seconds 2
 'Closed only note.txt Notepad and exact smoke-fixture Explorer windows.'; exit
}
if ($Action -eq 'StartApp') {
 $exe = Join-Path $PSScriptRoot '../src/FlowDesk.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/FlowDesk.App.exe'
 Start-Process -FilePath $exe
 Start-Sleep -Seconds 3
 'FlowDesk launched.'; exit
}
$process = Get-Process FlowDesk.App -ErrorAction Stop
if ($Action -eq 'AssertSingleton') {
 if (@($process).Count -ne 1) { throw 'Expected exactly one FlowDesk process.' }
 'PASS: second launch redirected; exactly one FlowDesk process remains.'; exit
}
if ($Action -eq 'CloseApp') {
 [void]$process.CloseMainWindow()
 if (-not $process.WaitForExit(10000)) { throw 'FlowDesk did not close in ten seconds.' }
 'FlowDesk closed normally and flushed persistence.'; exit
}
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if ($Action -eq 'Shot') {
 if (-not $OutputPath -or -not (Test-Path -LiteralPath (Split-Path $OutputPath))) { throw 'An existing screenshot directory is required.' }
 $r = New-Object SmokeNative+Rect
 [void][SmokeNative]::GetWindowRect($process.MainWindowHandle, [ref]$r)
 $bitmap = New-Object System.Drawing.Bitmap(($r.Right-$r.Left), ($r.Bottom-$r.Top))
 $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
 $dc = $graphics.GetHdc()
 try { [void][SmokeNative]::PrintWindow($process.MainWindowHandle, $dc, 2) } finally { $graphics.ReleaseHdc($dc) }
 $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
 $graphics.Dispose(); $bitmap.Dispose(); "Screenshot: $OutputPath"; exit
}
$all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
if ($Action -eq 'CloseDialog') {
 $dialog = @($all | Where-Object {
  $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $_.Current.Name -eq 'Workspace results'
 })[0]
 $buttons = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,
     [System.Windows.Automation.Condition]::TrueCondition)
 $close = @($buttons | Where-Object { $_.Current.Name -eq 'Close' })[0]
 $close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 'Closed workspace results dialog.'; exit
}
if ($Action -eq 'AssertMinimizeOff') {
 $toggle = @($all | Where-Object { $_.Current.Name -eq 'Minimize previous workspace windows' })[0]
 $state = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
 if ($state -ne 'Off') { throw 'Minimize previous setting did not persist as Off.' }
 'PASS: minimize previous workspace setting persisted as Off.'; exit
}
if ($Action -in @('ResizeNarrow','ResizeDefault')) {
 $transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
 if ($Action -eq 'ResizeNarrow') { $transform.Resize(720,520) } else { $transform.Resize(1100,720) }
 Start-Sleep -Seconds 1
 "Resized app: $Action"; exit
}
if ($Action -eq 'ScrollBottom') {
 foreach ($e in $all) {
  $pattern = $null
  if ($e.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$pattern) -and
      $pattern.Current.VerticallyScrollable) {
   $pattern.SetScrollPercent(-1,100); Start-Sleep -Milliseconds 500; 'Scrolled page to bottom.'; exit
  }
 }
 throw 'No vertically scrollable page found.'
}
if ($Action -in @('AssertLightTheme','AssertDarkTheme')) {
 $combo = @($all | Where-Object {
  $_.Current.Name -eq 'Theme' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox
 })[0]
 $selected = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
 $expected = if ($Action -eq 'AssertLightTheme') { 'Light' } else { 'Dark' }
 if ($selected.Count -ne 1 -or $selected[0].Current.Name -ne $expected) { throw "Expected persisted $expected theme." }
 "PASS: persisted app theme is $expected."; exit
}
if ($Action -eq 'SaveFixtures') {
 $selected = 0
 foreach ($e in $all) {
  if ($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::CheckBox) {
   $pattern = $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
   $want = $e.Current.Name -in @('Notepad', 'smoke-fixture')
   if ($want) { $selected++ }
   if (($pattern.Current.ToggleState -eq 'On') -ne $want) { $pattern.Toggle() }
  }
 }
 if ($selected -ne 2) { throw "Expected exactly two named fixture checkboxes, found $selected. Nothing saved." }
 foreach ($e in $all) {
  if ($e.Current.Name -eq 'Workspace name' -and $e.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
   $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('FlowDesk smoke test')
  }
 }
 foreach ($e in $all) {
  if ($e.Current.Name -eq 'Save workspace') {
   $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break
  }
 }
 Start-Sleep -Seconds 2
 'Saved only the two named fixtures as FlowDesk smoke test.'; exit
}
if ($Action -eq 'AssertWorkspace') {
 $found = @($all | Where-Object { $_.Current.Name -eq 'FlowDesk smoke test' })
 if ($found.Count -lt 1) { throw 'Persisted smoke workspace is not visible.' }
 'PASS: FlowDesk smoke test workspace is visible after restart.'; exit
}
if ($Action -eq 'Inspect') {
 foreach ($e in $all) { if ($e.Current.Name) { '{0} | {1} | enabled={2}' -f $e.Current.ControlType.ProgrammaticName,$e.Current.Name,$e.Current.IsEnabled } }; exit
}
$element = $null
foreach ($e in $all) {
 if ($e.Current.Name -ne $Name) { continue }
 if ($Action -eq 'SetText' -and $e.Current.ControlType -ne [System.Windows.Automation.ControlType]::Edit) { continue }
 if ($Action -eq 'Choose' -and $e.Current.ControlType -ne [System.Windows.Automation.ControlType]::ComboBox) { continue }
 if ($Action -eq 'Select' -and $e.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) { continue }
 if ($Action -in @('Click','Invoke') -and $e.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) { continue }
 $element = $e; break
}
if (-not $element) { throw "Control not found: $Name" }
switch ($Action) {
 'SetText' { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value) }
 'Toggle' { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
 'Select' { $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
 'Choose' { $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() }
 'Expand' { $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() }
 default { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
}
Start-Sleep -Milliseconds 750
"$Action succeeded: $Name"
