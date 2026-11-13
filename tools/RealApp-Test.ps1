param(
    [ValidateSet('Parse','Snapshot','Inspect','Full')][string]$Mode = 'Full',
    [string]$Filter = '*',
    [string]$Page = ''
)
$ErrorActionPreference = 'Stop'
if ($Mode -eq 'Parse') {
    # Parser-only check: never starts apps or touches UI; exits before any run-folder setup.
    $parseErrors = $null; $parseTokens = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$parseTokens, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        $parseErrors | ForEach-Object { '{0}:{1} {2}' -f $_.Extent.StartLineNumber, $_.Extent.StartColumnNumber, $_.Message }
        'PARSE FAILED: ' + $parseErrors.Count + ' errors'
        exit 1
    }
    'PARSE OK: ' + $parseTokens.Count + ' tokens, 0 errors'
    exit 0
}
$RootRepo = 'D:\Workspace\_projects\FlowDesk\flowdesk-app'
$RunDir = 'D:\Workspace\_projects\FlowDesk\sol-tasks\realtest-001'
$BuildDir = 'D:\Workspace\Builds\FlowDesk Builds\FlowDesk-2026-11-13'
$AppExe = Join-Path $BuildDir 'FlowDesk.App.exe'
$ShotDir = Join-Path $RunDir 'shots'
$LogPath = Join-Path $RunDir 'RealApp-Test.log'
$ChromeProfile = Join-Path $RunDir ('chrome-profile-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$NotesA = Join-Path $RunDir 'notes-a.txt'
$NotesB = Join-Path $RunDir 'notes-b.txt'
$NotesC = Join-Path $RunDir 'notes-c.txt'
$NotesC2 = Join-Path $RunDir 'notes-c2.txt'
$FolderA = Join-Path $RunDir 'folder-a'
$FolderB = Join-Path $RunDir 'folder-b'

New-Item -ItemType Directory -Force -Path $RunDir, $ShotDir | Out-Null
foreach ($f in @($NotesA,$NotesB,$NotesC,$NotesC2)) { Set-Content -LiteralPath $f -Value ('FlowDesk REALTEST-001 ' + [IO.Path]::GetFileNameWithoutExtension($f)) -Encoding UTF8 }
New-Item -ItemType Directory -Force -Path $FolderA, $FolderB | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $FolderA 'readme.txt'))) { Set-Content -LiteralPath (Join-Path $FolderA 'readme.txt') -Value 'fixture folder A' }
if (-not (Test-Path -LiteralPath (Join-Path $FolderB 'readme.txt'))) { Set-Content -LiteralPath (Join-Path $FolderB 'readme.txt') -Value 'fixture folder B' }
Set-Content -LiteralPath $LogPath -Value ('REALTEST-001 run ' + (Get-Date -Format o)) -Encoding UTF8

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class RTMon { public string Device=""; public string DeviceName=""; public int X,Y,W,H,WX,WY,WW,WH,Dpi; public bool Primary; }
public class RTWin {
 public long Handle; public uint Pid; public uint ChildPid; public string Title=""; public string Class=""; public string Device=""; public int Dpi;
 public int X,Y,W,H,NormalX,NormalY,NormalW,NormalH; public string Show="";
 public bool Min,Max,Visible; public int Right { get { return X+W; } } public int Bottom { get { return Y+H; } }
}
public static class RT {
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
 delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsWindowVisible2(IntPtr h);
 [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorProc cb, IntPtr data);
 delegate bool MonitorProc(IntPtr m, IntPtr hdc, IntPtr rc, IntPtr data);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
 [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="GetMonitorInfoW")] static extern bool GetMonitorInfoEx(IntPtr m, ref MONITORINFOEX mi);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
 [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
 [DllImport("user32.dll", SetLastError=true)] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
 [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
 [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
 [DllImport("Shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int dpiType, out uint dpiX, out uint dpiY);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
 [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct MONITORINFOEX {
  public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string szDevice;
 }
 [StructLayout(LayoutKind.Sequential)] public struct WINDOWPLACEMENT {
  public int length; public int flags; public int showCmd;
  public POINT ptMinPosition; public POINT ptMaxPosition; public RECT rcNormalPosition;
 }
 /// <summary>Per-Monitor-V2 awareness, so rectangles match FlowDesk's (DPI-unaware PowerShell sees scaled 2048 instead of 2560).</summary>
 public static bool EnsureDpiAwareness() {
  IntPtr want = new IntPtr(-4);
  SetProcessDpiAwarenessContext(want); // may fail when the process is already aware; the thread context is authoritative
  return AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), want);
 }
 static int DpiOf(IntPtr mon) { uint x, y; return GetDpiForMonitor(mon, 0, out x, out y) == 0 ? (int)x : 96; }
 static string DeviceOf(IntPtr mon) {
  var mi = new MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
  return mon != IntPtr.Zero && GetMonitorInfoEx(mon, ref mi) ? mi.szDevice : "";
 }
  public static uint ChildPid(IntPtr h) {
  uint found = 0, framePid; GetWindowThreadProcessId(h, out framePid);
  EnumChildWindows(h, delegate(IntPtr c, IntPtr l) {
   uint pid; GetWindowThreadProcessId(c, out pid);
   if (pid != 0 && pid != framePid) { found = pid; return false; }
   return true;
  }, IntPtr.Zero);
  return found;
 }
 static RTWin Read(IntPtr h) {
  var w = new RTWin(); w.Handle = (long)h;
  var sb = new StringBuilder(1024); GetWindowText(h, sb, 1024); w.Title = sb.ToString();
  var cb = new StringBuilder(256); GetClassName(h, cb, 256); w.Class = cb.ToString();
  uint pid; GetWindowThreadProcessId(h, out pid); w.Pid = pid;
  RECT r; GetWindowRect(h, out r); w.X = r.Left; w.Y = r.Top; w.W = r.Right-r.Left; w.H = r.Bottom-r.Top;
  w.Min = IsIconic(h); w.Max = IsZoomed(h); w.Visible = IsWindowVisible(h);
  w.Show = w.Min ? "Minimized" : w.Max ? "Maximized" : "Normal";
  IntPtr hm = MonitorFromWindow(h, 2);
  w.Device = DeviceOf(hm); w.Dpi = DpiOf(hm);
  // UWP: the real app is a child process of ApplicationFrameHost; identify it for ownership checks.
  if (w.Class == "ApplicationFrameWindow") w.ChildPid = ChildPid(h);
  // Normal (restore) rect exactly like FlowDesk: outer rect when normal, WINDOWPLACEMENT + work-area offset when not.
  w.NormalX = w.X; w.NormalY = w.Y; w.NormalW = w.W; w.NormalH = w.H;
  if (w.Min || w.Max) {
   var wp = new WINDOWPLACEMENT(); wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
   if (GetWindowPlacement(h, ref wp)) {
    int dx = 0, dy = 0;
    var mi = new MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
    if (hm != IntPtr.Zero && GetMonitorInfoEx(hm, ref mi)) { dx = mi.rcWork.Left - mi.rcMonitor.Left; dy = mi.rcWork.Top - mi.rcMonitor.Top; }
    w.NormalX = wp.rcNormalPosition.Left + dx; w.NormalY = wp.rcNormalPosition.Top + dy;
    w.NormalW = wp.rcNormalPosition.Right - wp.rcNormalPosition.Left; w.NormalH = wp.rcNormalPosition.Bottom - wp.rcNormalPosition.Top;
   }
  }
  return w;
 }
 public static List<RTWin> WindowsAll() {
  var list = new List<RTWin>();
  EnumWindows(delegate(IntPtr h, IntPtr l) {
   if (IsWindow(h)) {
    var w = Read(h);
    if (w.Title.Length > 0 || w.Class == "CabinetWClass") list.Add(w);
   }
   return true;
  }, IntPtr.Zero);
  return list;
 }
 public static List<RTWin> Windows() {
  var list = new List<RTWin>();
  EnumWindows(delegate(IntPtr h, IntPtr l) {
   if (IsWindow(h) && IsWindowVisible(h)) {
    var w = Read(h);
    if (w.Title.Length > 0 || w.Class == "CabinetWClass") list.Add(w);
   }
   return true;
  }, IntPtr.Zero);
  return list;
 }
 public static RTWin Window(long h) { return IsWindow((IntPtr)h) ? Read((IntPtr)h) : null; }
 public static bool SetPos(long h, int x, int y, int w, int hh) {
  if (IsIconic((IntPtr)h)) ShowWindow((IntPtr)h, 9);
  return SetWindowPos((IntPtr)h, IntPtr.Zero, x, y, w, hh, 0x0010 | 0x0004 | 0x0040);
 }
 public static void Top(long h) { SetWindowPos((IntPtr)h, IntPtr.Zero, 0,0,0,0, 0x0001 | 0x0002 | 0x0010 | 0x0040); }
 public static void Show(long h, int cmd) { ShowWindow((IntPtr)h, cmd); }
 public static void Close(long h) { PostMessage((IntPtr)h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
 public static long Foreground() { return (long)GetForegroundWindow(); }
 public static List<RTMon> Monitors() {
  var list = new List<RTMon>(); int i = 0;
  EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr m, IntPtr hdc, IntPtr rc, IntPtr data) {
   var mi = new MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
   if (GetMonitorInfoEx(m, ref mi)) {
    var d = new RTMon(); i++;
    d.Device = "Display " + i; d.DeviceName = mi.szDevice; d.Dpi = DpiOf(m);
    d.X = mi.rcMonitor.Left; d.Y = mi.rcMonitor.Top; d.W = mi.rcMonitor.Right-mi.rcMonitor.Left; d.H = mi.rcMonitor.Bottom-mi.rcMonitor.Top;
    d.WX = mi.rcWork.Left; d.WY = mi.rcWork.Top; d.WW = mi.rcWork.Right-mi.rcWork.Left; d.WH = mi.rcWork.Bottom-mi.rcWork.Top;
    d.Primary = (mi.dwFlags & 1) != 0; list.Add(d);
   }
   return true;
  }, IntPtr.Zero);
  return list;
 }
}
'@

# Must run before any monitor/window call: Windows PowerShell is DPI-unaware on a 125% display
# (it sees 2048x1152 where FlowDesk sees 2560x1440), which makes every monitor index unreliable.
$script:TaskStart = Get-Date
$script:DpiAware = [RT]::EnsureDpiAwareness()
$script:DpiAwareError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()

# ---------- logging and results ----------
$script:Results = New-Object System.Collections.ArrayList
function Log([string]$text) { $line = ('{0:HH:mm:ss} {1}' -f (Get-Date), $text); Add-Content -LiteralPath $LogPath -Value $line; Write-Host $line }
if (-not $script:DpiAware) {
    Log ('BLOCKED: process DPI awareness is not per-monitor-v2 after SetProcessDpiAwarenessContext (error=' + $script:DpiAwareError + '); rects would be scaled')
    exit 2
}
function Add-Result([string]$step,[string]$status,[string]$expected,[string]$actual,[string]$evidence) {
    [void]$script:Results.Add([pscustomobject]@{ Step=$step; Status=$status; Expected=$expected; Actual=$actual; Evidence=$evidence })
    Log ('RESULT ' + $status + ' | ' + $step + ' | expected: ' + $expected + ' | actual: ' + $actual + ' | ' + $evidence)
}
function Get-StatusCount([string]$s) { @($script:Results | Where-Object Status -eq $s).Count }
function Save-Shot([string]$name) {
    try {
        $p = Get-Process -Name FlowDesk.App -ErrorAction Stop | Select-Object -First 1
        if ($p.MainWindowHandle -eq 0) { return '' }
        $r = [RT]::Window([int64]$p.MainWindowHandle)
        if (-not $r) { return '' }
        $bmp = New-Object System.Drawing.Bitmap($r.W, $r.H)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $dc = $g.GetHdc()
        Add-Type @'
using System; using System.Runtime.InteropServices;
public static class RTShot { [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags); }
'@ -ErrorAction SilentlyContinue
        try { [void][RTShot]::PrintWindow([IntPtr][int64]$p.MainWindowHandle, $dc, 2) } finally { $g.ReleaseHdc($dc) }
        $path = Join-Path $ShotDir $name
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        return $path
    } catch { return '' }
}

# ---------- UIA ----------
function Get-AppProcess { @(Get-Process -Name FlowDesk.App -ErrorAction SilentlyContinue) | Select-Object -First 1 }
function Get-AppRoot {
    $p = Get-AppProcess
    if (-not $p -or $p.MainWindowHandle -eq 0) { return $null }
    [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
}
function Get-UiAll([System.Windows.Automation.AutomationElement]$root) {
    if (-not $root) { return @() }
    $c = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    @($c)
}
function Get-DesktopAll {
    $c = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    @($c)
}
function Get-ByName([string]$name, $type, [switch]$Desktop) {
    $all = if ($Desktop) { Get-DesktopAll } else { Get-UiAll (Get-AppRoot) }
    if ($type) { @($all | Where-Object { $_.Current.Name -eq $name -and $_.Current.ControlType -eq $type }) }
    else { @($all | Where-Object { $_.Current.Name -eq $name }) }
}
function Get-ByContains([string]$text, $type, [switch]$Desktop) {
    $all = if ($Desktop) { Get-DesktopAll } else { Get-UiAll (Get-AppRoot) }
    if ($type) { @($all | Where-Object { $_.Current.Name -like ('*' + $text + '*') -and $_.Current.ControlType -eq $type }) }
    else { @($all | Where-Object { $_.Current.Name -like ('*' + $text + '*') }) }
}
function Invoke-Element($e) {
    if (-not $e) { throw 'UIA element is null' }
    $pat = $null
    if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke(); return }
    if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) { $pat.Select(); return }
    if ($e.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { $pat.Toggle(); return }
    if ($e.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pat)) { $pat.Expand(); return }
    throw ('No actionable pattern on ' + $e.Current.ControlType.ProgrammaticName + ' ' + $e.Current.Name)
}
function Select-Element($e) {
    $pat = $null
    if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) { $pat.Select(); return }
    if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke(); return }
    throw 'Element cannot be selected.'
}
function Get-ToggleState($e) {
    $pat = $null
    if ($e.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { return [string]$pat.Current.ToggleState }
    return 'Unknown'
}
function Set-Toggle($e, [bool]$on) {
    $state = Get-ToggleState $e
    if (($state -eq 'On') -ne $on) { $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
}
function Wait-Ui([scriptblock]$test, [int]$seconds = 20) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        $v = & $test
        if ($v) { return $v }
        Start-Sleep -Milliseconds 400
    }
    return $null
}
function Navigate([string]$page) {
    $item = @(Get-ByContains $page ([System.Windows.Automation.ControlType]::ListItem) | Where-Object { $_.Current.Name -eq $page })[0]
    if (-not $item) { $item = @(Get-ByName $page ([System.Windows.Automation.ControlType]::ListItem))[0] }
    if (-not $item) { throw ('Navigation item not found: ' + $page) }
    Select-Element $item
    Start-Sleep -Milliseconds 700
}
function Find-Ancestor($e, [scriptblock]$predicate) {
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $node = $e
    for ($i = 0; $i -lt 14; $i++) {
        $node = $walker.GetParent($node)
        if (-not $node) { return $null }
        if ($node -eq [System.Windows.Automation.AutomationElement]::RootElement) { return $null }
        if (& $predicate $node) { return $node }
    }
    return $null
}
function Get-TextOf($e) { @($e.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name } | ForEach-Object { $_.Current.Name }) }
function Get-FocusRuleRows([string]$removeName) {
    $rows = @()
    $buttons = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.Name -eq $removeName -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button })
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    foreach ($btn in $buttons) {
        # Nearest ancestor that is one card tall and holds exactly this Remove button; avoids page-wide texts.
        $card = $null; $node = $btn
        for ($i = 0; $i -lt 14; $i++) {
            $node = $walker.GetParent($node)
            if (-not $node -or $node -eq [System.Windows.Automation.AutomationElement]::RootElement) { break }
            $r = $node.Current.BoundingRectangle
            if ($r.Height -le 0 -or $r.Height -gt 140) { continue }
            $count = @($node.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq $removeName }).Count
            if ($count -eq 1) { $card = $node; break }
        }
        if (-not $card) { $rows += '<unreadable rule>'; continue }
        $names = @(Get-TextOf $card | Where-Object { $_ -ne $removeName })
        $rows += ($names -join ' | ')
    }
    return $rows
}
# WinUI's ItemsControl exposes all workspace cards under one Pane (Border/Grid have no automation peers),
# so an ancestor search cannot isolate a card. The cards are laid out as fixed ~125 px rows: match the
# name Text to the Open / Workspace actions button on the same visual row instead.
function Get-WorkspaceRowControl([string]$name, [string[]]$controlNames, $type) {
    $texts = @(Get-ByName $name ([System.Windows.Automation.ControlType]::Text))
    if ($texts.Count -eq 0) { return $null }
    $anchor = @($texts | Where-Object { -not $_.Current.IsOffscreen })[0]
    if (-not $anchor) { return $null }
    $ar = $anchor.Current.BoundingRectangle
    if ($ar.Width -le 0 -or $ar.Height -le 0) { return $null }
    $anchorCenter = $ar.Top + ($ar.Height / 2)
    $candidates = @(Get-UiAll (Get-AppRoot) | Where-Object {
        $_.Current.ControlType -eq $type -and
        ($controlNames -contains $_.Current.Name) -and
        $_.Current.BoundingRectangle.X -gt $ar.X
    })
    $scored = @($candidates | ForEach-Object {
        $r = $_.Current.BoundingRectangle
        [pscustomobject]@{ Element = $_; Distance = [Math]::Abs(($r.Top + ($r.Height / 2)) - $anchorCenter); Rect = $r }
    } | Sort-Object Distance)
    if ($scored.Count -eq 0) { return $null }
    # Rows are ~125 px apart; anything farther than half a row is not the same card.
    if ($scored[0].Distance -gt 62) {
        Log ('WARN no same-row match for ' + $name + ' / ' + ($controlNames -join '|') + ' (nearest ' + [int]$scored[0].Distance + 'px)')
        return $null
    }
    if ($scored.Count -gt 1 -and ([Math]::Abs($scored[1].Distance - $scored[0].Distance) -lt 8)) {
        Log ('WARN ambiguous row match for ' + $name + ' / ' + ($controlNames -join '|') + ' (two buttons within 8px)')
        return $null
    }
    Log ('ROW ' + $name + ' -> ' + $scored[0].Element.Current.Name + ' at ' + [int]$scored[0].Rect.X + ',' + [int]$scored[0].Rect.Y + ' distance=' + [int]$scored[0].Distance)
    return $scored[0].Element
}
function Get-WorkspaceOpenButton([string]$name) {
    return Get-WorkspaceRowControl $name @('Open') ([System.Windows.Automation.ControlType]::Button)
}
function Get-WorkspaceActionsButton([string]$name) {
    $b = Get-WorkspaceRowControl $name @('Workspace actions') ([System.Windows.Automation.ControlType]::Button)
    if ($b) { return $b }
    # Some WinUI builds expose the button's ToolTip text instead of its AutomationProperties.Name.
    return Get-WorkspaceRowControl $name @('More workspace actions') ([System.Windows.Automation.ControlType]::Button)
}
function Open-Workspace([string]$name) {
    Navigate 'Workspaces'
    $btn = Get-WorkspaceOpenButton $name
    if (-not $btn) { throw ('Open button not found for workspace ' + $name) }
    Invoke-Element $btn
    Start-Sleep -Seconds 1
}
function Get-StatusTexts {
    $all = Get-UiAll (Get-AppRoot)
    @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -like '*windows in place*' } | ForEach-Object { $_.Current.Name })
}
function Wait-Report([string]$wsName, [int]$timeout = 75) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $sawOpening = $false
    while ($sw.Elapsed.TotalSeconds -lt $timeout) {
        $opening = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -like 'Opening * of *' })
        if ($opening.Count -gt 0) { $sawOpening = $true }
        elseif ($sawOpening -or $sw.Elapsed.TotalSeconds -gt 2.5) {
            $t = @(Get-StatusTexts | Where-Object { $_ -like ($wsName + ':*') })
            if ($t.Count -gt 0) { return $t[0] }
        }
        Start-Sleep -Milliseconds 600
    }
    return ''
}
function Read-ReportDialog {
    $btn = @(Get-ByName 'Details' ([System.Windows.Automation.ControlType]::Button))[0]
    if (-not $btn) { return @() }
    Invoke-Element $btn
    Start-Sleep -Seconds 1
    $dlg = @(Get-ByName 'Workspace results' ([System.Windows.Automation.ControlType]::Window))[0]
    if (-not $dlg) { return @() }
    $texts = Get-TextOf $dlg
    $close = @($dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq 'Close' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button })[0]
    if ($close) { Invoke-Element $close }
    Start-Sleep -Milliseconds 500
    return $texts
}

# ---------- window helpers ----------
function Get-RTWindows { @([RT]::Windows()) }
function Get-ProcInfo([uint32]$procId) {
    try { $p = Get-Process -Id $procId -ErrorAction Stop; return [pscustomobject]@{ Name = $p.ProcessName; Start = $p.StartTime; Path = $(try { $p.Path } catch { '' }) } }
    catch { return [pscustomobject]@{ Name = ''; Start = $null; Path = '' } }
}
function Get-AppProcessId($w) {
    if ($w -and $w.Class -eq 'ApplicationFrameWindow' -and $w.ChildPid -ne 0) { return [uint32]$w.ChildPid }
    if ($w) { return [uint32]$w.Pid }
    return [uint32]0
}
function Format-Window($w) {
    if (-not $w) { return 'none' }
    $pi = Get-ProcInfo $w.Pid
    return ('hwnd=0x{0:X} pid={1} proc={2} start={3:HH:mm:ss} class={4} title="{5}" show={6} rect={7},{8} {9}x{10} normal={11},{12} {13}x{14} dpi={15} monitor={16}' -f `
        [int64]$w.Handle,$w.Pid,$pi.Name,$pi.Start,$w.Class,$w.Title,$w.Show,$w.X,$w.Y,$w.W,$w.H,
        $w.NormalX,$w.NormalY,$w.NormalW,$w.NormalH,$w.Dpi,$w.Device)
}
function Log-Window([string]$label, $w) { Log ($label + ': ' + (Format-Window $w)) }
function Monitor-IndexByDevice([string]$device, $monitors) {
    for ($i = 0; $i -lt $monitors.Count; $i++) {
        if ($device -and [string]::Equals($monitors[$i].DeviceName, $device, [StringComparison]::OrdinalIgnoreCase)) { return $i }
    }
    return -1
}
function Monitor-IndexOfNormal($w, $monitors) {
    if (-not $w) { return -1 }
    $cx = $w.NormalX + [int]($w.NormalW / 2); $cy = $w.NormalY + [int]($w.NormalH / 2)
    for ($i = 0; $i -lt $monitors.Count; $i++) {
        $m = $monitors[$i]
        if ($cx -ge $m.X -and $cx -lt ($m.X + $m.W) -and $cy -ge $m.Y -and $cy -lt ($m.Y + $m.H)) { return $i }
    }
    return -1
}
function Get-SelectedState($e) {
    $pat = $null
    try {
        if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) { return [bool]$pat.Current.IsSelected }
        if ($e.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { return ($pat.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) }
    } catch { }
    return $null
}
function Get-CandidateDetails($cbs) {
    foreach ($cb in @($cbs)) {
        $name = ''; $id = ''; $help = ''; $x = -1; $y = -1; $enabled = $false; $on = 'Unknown'
        try { $name = $cb.Current.Name } catch { }
        try { $id = [string]$cb.Current.AutomationId } catch { }
        try { $help = [string]$cb.Current.HelpText } catch { }
        try { $r = $cb.Current.BoundingRectangle; $x = [int]$r.X; $y = [int]$r.Y } catch { }
        try { $enabled = [bool]$cb.Current.IsEnabled } catch { }
        try { $on = Get-ToggleState $cb } catch { }
        $texts = @(); try { $texts = @(Get-TextOf $cb) } catch { }
        [pscustomobject]@{ Element = $cb; Name = $name; Id = $id; Texts = $texts; Help = $help; X = $x; Y = $y; Enabled = $enabled; Toggle = $on }
    }
}
function Log-Candidates($details) {
    foreach ($c in @($details)) {
        Log ('CANDIDATE id="' + $c.Id + '" name="' + $c.Name + '" texts=[' + ($c.Texts -join ' | ') + '] help="' + $c.Help + '" rect=' + $c.X + ',' + $c.Y + ' enabled=' + $c.Enabled + ' toggle=' + $c.Toggle)
    }
}
function Select-SaveCandidateById($details, [int64]$handle, [string]$title, [string]$group) {
    $id = 'window-' + $handle.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $candidates = @($details | Where-Object { $_.Id -eq $id })
    Log ('CANDIDATES ' + $group + ': ' + $candidates.Count + ' with AutomationId "' + $id + '"; expected title "' + $title + '"')
    if ($candidates.Count -eq 0) { throw ('BLOCKED: no Save candidate with AutomationId "' + $id + '" for ' + $group + '; window automation wiring is missing') }
    if ($candidates.Count -gt 1) { throw ('BLOCKED: ' + $candidates.Count + ' Save candidates share AutomationId "' + $id + '" for ' + $group) }
    $c = $candidates[0]
    if (-not $c.Enabled) { throw ('BLOCKED: Save candidate ' + $id + ' for ' + $group + ' is disabled') }
    $titleOk = ($title -and ($c.Name -eq $title -or $c.Help -eq $title -or @($c.Texts | Where-Object { $_ -eq $title }).Count -gt 0))
    if (-not $titleOk) { throw ('BLOCKED: Save candidate ' + $id + ' for ' + $group + ' does not show expected title "' + $title + '"') }
    return $c.Element
}
function Sync-ExpectedLive([string]$phase, $targets, $expected, $monitors) {
    foreach ($t in @($targets)) {
        $w = [RT]::Window([int64]$t.Win.Handle)
        if (-not $w) { Log ($phase + ' ' + $t.Name + ': window 0x' + ('{0:X}' -f [int64]$t.Win.Handle) + ' is gone'); continue }
        Log-Window ($phase + ' ' + $t.Name) $w
        if ($expected.Contains($t.Name)) {
            $e = $expected[$t.Name]
            $drift = @([Math]::Abs($w.NormalX - $e.X), [Math]::Abs($w.NormalY - $e.Y), [Math]::Abs($w.NormalW - $e.W), [Math]::Abs($w.NormalH - $e.H))
            if (@($drift | Where-Object { $_ -gt 0 }).Count -gt 0) {
                Log ($phase + ' ' + $t.Name + ' drift: normal rect ' + $e.X + ',' + $e.Y + ' ' + $e.W + 'x' + $e.H + ' -> ' + $w.NormalX + ',' + $w.NormalY + ' ' + $w.NormalW + 'x' + $w.NormalH + ' delta=' + ($drift -join ','))
            }
        }
        $appPid = Get-AppProcessId $w; $appPi = Get-ProcInfo $appPid
        $expected[$t.Name] = [pscustomobject]@{
            Handle = [int64]$w.Handle; Pid = $w.Pid; AppPid = [uint32]$appPid; Process = $appPi.Name; Path = $appPi.Path
            Title = $w.Title; Class = $w.Class; Device = $w.Device
            X = $w.NormalX; Y = $w.NormalY; W = $w.NormalW; H = $w.NormalH
            Show = $w.Show; Min = $w.Min; Max = $w.Max; Monitor = (Monitor-IndexOfNormal $w $monitors)
        }
    }
}
function Resolve-RestoredWindow([string]$name, $exp, $now, $baselineHandles) {
    $raw = switch ($name) {
        'Notepad' { @($now | Where-Object { $_.Title -like '*notes-a.txt*' }) }
        'Paint' { @($now | Where-Object { $_.Title -like '*Paint*' -and $_.Class -ne 'Chrome_WidgetWin_1' }) }
        'Calculator' { @($now | Where-Object { $_.Title -eq 'Calculator' }) }
        'folder-a' { $h = Get-ExplorerWindowFor $FolderA; if ($h) { @([RT]::Window([int64]$h)) } else { @() } }
        'Chrome' {
            try {
                $pids = @(Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object { $_.CommandLine -like ('*' + $ChromeProfile + '*') } | ForEach-Object { [uint32]$_.ProcessId })
                @($now | Where-Object { $pids -contains [uint32]$_.Pid -and $_.Class -eq 'Chrome_WidgetWin_1' })
            } catch { @() }
        }
        default { @() }
    }
    $ok = @()
    foreach ($w in @($raw)) {
        if (-not $w -or ($baselineHandles -contains [int64]$w.Handle)) { continue }
        $appPid = Get-AppProcessId $w; $pi = Get-ProcInfo $appPid
        if ($exp.Process -and $pi.Name -ne $exp.Process) { continue }
        if ($exp.Path -and $pi.Path -and $pi.Path -ne $exp.Path) { continue }
        # Restored apps must be a newly started process (Calculator: its child app process), not a user instance.
        if ($name -ne 'folder-a' -and -not ($pi.Start -and $pi.Start -ge $script:TaskStart.AddSeconds(-15))) { continue }
        $ok += $w
    }
    if ($ok.Count -eq 1) { return [pscustomobject]@{ Window = $ok[0]; Reason = '' } }
    if ($ok.Count -eq 0) { return [pscustomobject]@{ Window = $null; Reason = ('no new owned ' + $name + ' window; user/baseline windows excluded') } }
    $titles = ($ok | ForEach-Object { '0x' + ('{0:X}' -f [int64]$_.Handle) }) -join ','
    return [pscustomobject]@{ Window = $null; Reason = ('ambiguous: ' + $ok.Count + ' new ' + $name + ' windows [' + $titles + ']; refusing adoption') }
}
function Find-RTWindow([scriptblock]$match, [hashtable]$before = @{}, [int]$timeout = 20, [int]$poll = 200) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeout) {
        foreach ($w in (Get-RTWindows)) {
            if ($before.ContainsKey([int64]$w.Handle)) { continue }
            if (& $match $w) { return $w }
        }
        Start-Sleep -Milliseconds $poll
    }
    return $null
}
function Wait-NoWindow([scriptblock]$match, [int]$timeout = 10) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeout) {
        $found = $false
        foreach ($w in (Get-RTWindows)) { if (& $match $w) { $found = $true; break } }
        if (-not $found) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}
function Rect-Delta($w, [int]$x, [int]$y, [int]$ww, [int]$hh) {
    return @([Math]::Abs($w.X - $x), [Math]::Abs($w.Y - $y), [Math]::Abs($w.W - $ww), [Math]::Abs($w.H - $hh))
}
function Snapshot-UserWindows($ownedHandles, $excludedPids) {
    $snap = @{}
    foreach ($w in (Get-RTWindows)) {
        if ($ownedHandles -contains [int64]$w.Handle) { continue }
        if ($excludedPids -contains [uint32]$w.Pid) { continue }
        $pi = Get-ProcInfo $w.Pid
        if ($pi.Name -eq 'FlowDesk.App') { continue }
        $snap[[int64]$w.Handle] = [pscustomobject]@{ Title=$w.Title; Process=$pi.Name; Pid=[uint32]$w.Pid; X=$w.X; Y=$w.Y; W=$w.W; H=$w.H; Min=$w.Min; Max=$w.Max }
    }
    return $snap
}
function Compare-UserWindows($before, $ownedHandles, $excludedPids) {
    $after = Snapshot-UserWindows $ownedHandles $excludedPids
    $issues = New-Object System.Collections.ArrayList
    foreach ($k in $before.Keys) {
        if (-not $after.ContainsKey($k)) { [void]$issues.Add(('window 0x{0:X} ({1}) disappeared' -f $k, $before[$k].Title)); continue }
        $b = $before[$k]; $a = $after[$k]
        if ($b.X -ne $a.X -or $b.Y -ne $a.Y -or $b.W -ne $a.W -or $b.H -ne $a.H) { [void]$issues.Add(('window 0x{0:X} ({1}) moved {2},{3},{4},{5} -> {6},{7},{8},{9}' -f $k,$b.Title,$b.X,$b.Y,$b.W,$b.H,$a.X,$a.Y,$a.W,$a.H)) }
        if ($b.Min -ne $a.Min) { [void]$issues.Add(('window 0x{0:X} ({1}) minimized {2} -> {3}' -f $k,$b.Title,$b.Min,$a.Min)) }
    }
    return @($issues)
}

# ---------- app launching ----------
$script:Owned = New-Object System.Collections.ArrayList
$script:FlowDeskBaselinePids = @()
$script:FlowDeskOwnedPids = New-Object System.Collections.ArrayList
$script:FlowDeskOwnedStart = @{}
function Add-Owned([string]$group, $w, $proc) {
    $pi = Get-ProcInfo $w.Pid
    $appPid = Get-AppProcessId $w
    $appPi = Get-ProcInfo $appPid
    $same = @($script:Owned | Where-Object { [int64]$_.Hwnd -eq [int64]$w.Handle })[0]
    if ($same) {
        if ([uint32]$same.Pid -ne [uint32]$w.Pid -or $same.Start -ne $appPi.Start) {
            Log ('REFUSE handle 0x' + ('{0:X}' -f [int64]$w.Handle) + ': recorded pid=' + $same.Pid + ' start=' + $same.Start + ' now pid=' + $w.Pid + ' start=' + $appPi.Start)
            return $w
        }
        Log ('OWNED again ' + $group + ' hwnd=0x' + ('{0:X}' -f [int64]$w.Handle) + ' pid=' + $w.Pid + ' (same owned window)')
        return $w
    }
    [void]$script:Owned.Add([pscustomobject]@{ Group=$group; Hwnd=[int64]$w.Handle; Pid=[uint32]$w.Pid; AppPid=[uint32]$appPid; Process=$appPi.Name; Start=$appPi.Start; Path=$appPi.Path; Class=$w.Class; Title=$w.Title })
    Log ('OWNED ' + $group + ' hwnd=0x' + ('{0:X}' -f [int64]$w.Handle) + ' pid=' + $w.Pid + ' appPid=' + $appPid + ' process=' + $appPi.Name + ' start=' + $(if($appPi.Start){$appPi.Start.ToString('HH:mm:ss')}else{'?'}) + ' title=' + $w.Title)
    return $w
}
function Get-OwnedHandles { @($script:Owned | ForEach-Object { [int64]$_.Hwnd }) }
function Get-FlowDeskProcesses { @(Get-Process -Name FlowDesk.App -ErrorAction SilentlyContinue | Sort-Object StartTime) }
function Register-FlowDeskOwned([uint32[]]$ids) {
    foreach ($procId in @($ids | Select-Object -Unique)) {
        $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        if ($script:FlowDeskBaselinePids -contains [uint32]$procId) {
            Log ('FlowDesk pid=' + $procId + ' existed before the test; leaving it alone')
            continue
        }
        if ($script:FlowDeskOwnedPids -contains [uint32]$procId) { continue }
        [void]$script:FlowDeskOwnedPids.Add([uint32]$procId)
        $script:FlowDeskOwnedStart[[uint32]$procId] = $p.StartTime
        Log ('FLOWDESK owned pid=' + $procId + ' start=' + $p.StartTime.ToString('HH:mm:ss'))
    }
}
function Test-RecordedProcess($rec) {
    if (-not $rec) { return $false }
    $p = Get-Process -Id $rec.AppPid -ErrorAction SilentlyContinue
    if (-not $p) { return $false }
    try { return ($p.StartTime -eq $rec.Start) } catch { return $false }
}
function Close-RTWindowOwned($w, [string]$reason) {
    if (-not $w) { return $true }
    $rec = @($script:Owned | Where-Object { [int64]$_.Hwnd -eq [int64]$w.Handle })[0]
    if (-not $rec) {
        Log ('WARN refuse close (' + $reason + '): window has no ownership record')
        return $false
    }
    if ($rec -and [uint32]$rec.Pid -ne [uint32]$w.Pid) {
        Log ('WARN refuse close (' + $reason + ') handle 0x' + ('{0:X}' -f [int64]$w.Handle) + ': owned pid=' + $rec.Pid + ' now pid=' + $w.Pid)
        return $false
    }
    if ((Get-AppProcessId $w) -ne $rec.AppPid -or -not (Test-RecordedProcess $rec)) {
        Log ('WARN refuse close (' + $reason + ') handle 0x' + ('{0:X}' -f [int64]$w.Handle) + ': recorded process start no longer matches')
        return $false
    }
    $ok = Close-OwnedWindow $w 8
    Log ('CLOSE (' + $reason + ') ' + (Format-Window $w) + ' ok=' + $ok)
    return $ok
}
function Test-TaskOwnedProcess([uint32]$procId) {
    foreach ($rec in @($script:Owned | Where-Object { $_.Process -notin @('explorer','ApplicationFrameHost','CalculatorApp') -and [uint32]$_.AppPid -eq $procId })) {
        if (Test-RecordedProcess $rec) { return $true }
    }
    $pi = Get-ProcInfo $procId
    if (-not ($pi.Start -and $pi.Start -ge $script:TaskStart.AddSeconds(-10))) { return $false }
    try {
        $cmd = (Get-CimInstance Win32_Process -Filter ('ProcessId=' + $procId) -ErrorAction Stop).CommandLine
        # Proof it opened a fixture file from this run folder, not a user window that merely shares a title.
        return ($cmd -and $cmd -like ('*' + $RunDir + '*'))
    } catch { return $false }
}
function Close-FixtureWindows([string]$reason) {
    $patterns = @('*notes-a.txt*','*notes-b.txt*','*notes-c.txt*','*notes-c2.txt*')
    foreach ($w in @([RT]::WindowsAll() | Where-Object { $t = $_.Title; @($patterns | Where-Object { $t -like $_ }).Count -gt 0 })) {
        if (-not (Test-TaskOwnedProcess ([uint32]$w.Pid))) {
            Log ('WARN leave fixture-looking window with unproven ownership: ' + (Format-Window $w))
            continue
        }
        Add-Owned 'Fixture-cleanup' $w $null | Out-Null
        [void](Close-RTWindowOwned $w ($reason + ' fixture'))
    }
}
function Stop-OwnedFlowDesk([int]$timeoutSeconds = 8) {
    foreach ($procId in @($script:FlowDeskOwnedPids | Sort-Object -Descending)) {
        $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        $expectedStart = $script:FlowDeskOwnedStart[[uint32]$procId]
        if (-not $expectedStart -or $p.StartTime -ne $expectedStart) {
            Log ('WARN not closing pid=' + $procId + ': process start no longer matches the recorded instance')
            continue
        }
        Log ('Closing test-owned FlowDesk pid=' + $procId + ' hwnd=0x' + ('{0:X}' -f $p.MainWindowHandle))
        if ($p.MainWindowHandle -ne 0) { [void][RT]::Close([int64]$p.MainWindowHandle) }
        if (-not $p.WaitForExit($timeoutSeconds * 1000)) {
            Log ('FlowDesk pid=' + $procId + ' ignored close; stopping it')
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 300
    }
    $left = @(Get-FlowDeskProcesses | Where-Object { $script:FlowDeskOwnedPids -contains [uint32]$_.Id })
    $others = @(Get-FlowDeskProcesses | Where-Object { $script:FlowDeskOwnedPids -notcontains [uint32]$_.Id })
    foreach ($p in $others) { Log ('FlowDesk pid=' + $p.Id + ' was not started by this test; leaving it running') }
    return $left.Count
}
function Get-ProcessPid($p) { if ($p) { return [uint32]$p.Id } else { return [uint32]0 } }
function Launch-Notepad([string]$file, [string]$group) {
    $before = @{}; foreach ($w in (Get-RTWindows)) { $before[[int64]$w.Handle] = $true }
    $started = Get-Date
    $p = Start-Process -FilePath notepad.exe -ArgumentList ('"' + $file + '"') -PassThru
    $leaf = [IO.Path]::GetFileName($file)
    $launchedPid = Get-ProcessPid $p
    $w = Find-RTWindow { param($x) ($x.Title -like ('*' + $leaf + '*')) -or ($launchedPid -ne 0 -and [uint32]$x.Pid -eq [uint32]$launchedPid) } $before 30
    if (-not $w) {
        # Packaged Notepad can surface its window after the first scan. Accept a matching title only
        # when its process started with this launch, so a window we did not start is never adopted.
        foreach ($x in (Get-RTWindows)) {
            if ($x.Title -notlike ('*' + $leaf + '*')) { continue }
            $pi2 = Get-ProcInfo $x.Pid
            if ($pi2.Start -and $pi2.Start -ge $started.AddSeconds(-4)) { $w = $x; break }
        }
    }
    if (-not $w) { return $null }
    $pi = Get-ProcInfo $w.Pid
    if ($pi.Start -and $pi.Start -lt $started.AddSeconds(-4)) { Log ('NOTE Notepad window older than launch: ' + $w.Title) }
    Add-Owned $group $w $p | Out-Null
    return $w
}
function Launch-Paint {
    $before = @{}; foreach ($w in (Get-RTWindows)) { $before[[int64]$w.Handle] = $true }
    $p = Start-Process -FilePath mspaint.exe -PassThru
    $w = Find-RTWindow { param($x) ($x.Title -like '*Paint*') -and ($x.Class -ne 'Chrome_WidgetWin_1') } $before 25
    if (-not $w) { return $null }
    Add-Owned 'Paint-A' $w $p | Out-Null
    return $w
}
function Launch-PaintB {
    $before = @{}; foreach ($w in (Get-RTWindows)) { $before[[int64]$w.Handle] = $true }
    $p = Start-Process -FilePath mspaint.exe -PassThru
    $w = Find-RTWindow { param($x) ($x.Title -like '*Paint*') -and ($x.Class -ne 'Chrome_WidgetWin_1') } $before 25
    if (-not $w) { return $null }
    Add-Owned 'Paint-B' $w $p | Out-Null
    return $w
}
function Launch-Calculator {
    $before = @{}; foreach ($w in (Get-RTWindows)) { $before[[int64]$w.Handle] = $true }
    $priorCalc = @(Get-RTWindows | Where-Object { $_.Title -eq 'Calculator' })
    $p = Start-Process -FilePath calc.exe -PassThru
    $w = Find-RTWindow { param($x) $x.Title -eq 'Calculator' } $before 20
    if (-not $w) { return $null }
    $calcNow = @(Get-RTWindows | Where-Object { $_.Title -eq 'Calculator' })
    if ($priorCalc.Count -gt 0 -or $calcNow.Count -ne 1) { Log 'Calculator ownership uncertain (existing or multiple windows).'; return $null }
    Add-Owned 'Calculator' $w $p | Out-Null
    return $w
}
function Get-ExplorerWindowFor([string]$path) {
    $shell = New-Object -ComObject Shell.Application
    foreach ($f in $shell.Windows()) {
        try { if ($f.Document.Folder.Self.Path -eq $path) { return $f.HWND } } catch { }
    }
    return 0
}
function Launch-Explorer([string]$path, [string]$group, [int]$timeout = 15) {
    $before = @{}; foreach ($w in (Get-RTWindows)) { $before[[int64]$w.Handle] = $true }
    Start-Process -FilePath explorer.exe -ArgumentList ('"' + $path + '"') | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeout) {
        $h = Get-ExplorerWindowFor $path
        if ($h -ne 0 -and -not $before.ContainsKey([int64]$h)) {
            $w = [RT]::Window([int64]$h)
            if ($w) { Add-Owned $group $w $null | Out-Null; return $w }
        }
        Start-Sleep -Milliseconds 400
    }
    return $null
}
function Launch-Chrome {
    $exe = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
    if (-not (Test-Path -LiteralPath $exe)) { $exe = 'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe' }
    if (-not (Test-Path -LiteralPath $exe)) { throw 'chrome.exe not found' }
    if (Test-Path -LiteralPath $ChromeProfile) { throw 'Disposable Chrome profile already exists; preserve it and start a new run.' }
    $before = @{}; foreach ($w in (Get-RTWindows)) { $before[[int64]$w.Handle] = $true }
    Start-Process -FilePath $exe -ArgumentList ('--user-data-dir="' + $ChromeProfile + '" --no-first-run --no-default-browser-check about:blank https://example.com') | Out-Null
    Start-Sleep -Seconds 2
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 30) {
        $pids = @()
        try { $pids = @(Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object { $_.CommandLine -like ('*' + $ChromeProfile + '*') } | ForEach-Object { [uint32]$_.ProcessId }) } catch { }
        foreach ($w in (Get-RTWindows)) {
            if ($pids -contains [uint32]$w.Pid -and $w.Class -eq 'Chrome_WidgetWin_1' -and -not $before.ContainsKey([int64]$w.Handle)) {
                Add-Owned 'Chrome' $w $null | Out-Null
                return $w
            }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}
function Close-OwnedWindow($w, [int]$timeout = 10) {
    if (-not $w) { return $true }
    [RT]::Close([int64]$w.Handle)
    $ok = Wait-NoWindow { param($x) [int64]$x.Handle -eq [int64]$w.Handle } $timeout
    if (-not $ok) {
        $pw = [RT]::Window([int64]$w.Handle)
        if ($pw) { [RT]::Show([int64]$w.Handle, 6); [RT]::Close([int64]$w.Handle) }
        $ok = Wait-NoWindow { param($x) [int64]$x.Handle -eq [int64]$w.Handle } 5
    }
    return $ok
}
function Close-OwnedGroup([string]$group, [int]$timeout = 10) {
    $items = @($script:Owned | Where-Object Group -eq $group)
    $all = $true
    foreach ($o in $items) {
        $w = [RT]::Window([int64]$o.Hwnd)
        if ($w) { $r = Close-RTWindowOwned $w $group; if (-not $r) { $all = $false } }
    }
    return $all
}
function Close-AllApps {
    $groups = @($script:Owned | ForEach-Object Group | Select-Object -Unique)
    foreach ($g in $groups) { if ($g -eq 'Chrome') { continue }; [void](Close-OwnedGroup $g 8) }
    # Chrome: close every window of the temp profile, then force only its own PIDs if a window ignores close.
    $chromeItems = @($script:Owned | Where-Object Group -eq 'Chrome')
    foreach ($o in $chromeItems) { $w = [RT]::Window([int64]$o.Hwnd); if ($w) { [void](Close-RTWindowOwned $w 'chrome') } }
    try {
        $pids = @(Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object { $_.CommandLine -like ('*' + $ChromeProfile + '*') } | ForEach-Object { [int]($_.ProcessId) })
        foreach ($procId in $pids) { if (Get-Process -Id $procId -ErrorAction SilentlyContinue) { Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue } }
    } catch { }
}

# ---------- snapshot/inspect modes ----------
if ($Mode -eq 'Snapshot') {
    $mons = @([RT]::Monitors())
    'Monitors:'
    foreach ($m in $mons) { ' {0}: {1},{2} {3}x{4} work {5},{6} {7}x{8} primary={9}' -f $m.Device,$m.X,$m.Y,$m.W,$m.H,$m.WX,$m.WY,$m.WW,$m.WH,$m.Primary }
    'Windows:'
    Get-RTWindows | Where-Object { $_.Title -like $Filter -or $_.Class -like $Filter } | Sort-Object Y,X | Format-Table -AutoSize -Property Handle,Pid,Title,Class,X,Y,W,H,Min,Max
    $fg = [RT]::Foreground()
    $f = [RT]::Window($fg)
    'Foreground: 0x{0:X} {1} | {2}' -f $fg, $(if($f){$f.Class}else{''}), $(if($f){$f.Title}else{''})
    'Folders:'; $shell = New-Object -ComObject Shell.Application; foreach ($f in $shell.Windows()) { try { ' {0} | {1}' -f $f.HWND,$f.Document.Folder.Self.Path } catch { } }
    exit 0
}
if ($Mode -eq 'Inspect') {
    if ($Page) { Navigate $Page; Start-Sleep -Milliseconds 800 }
    $root = Get-AppRoot
    if (-not $root) { throw 'FlowDesk window not found' }
    $all = Get-UiAll $root
    foreach ($e in $all) {
        if (-not $e.Current.Name) { continue }
        if ($Filter -ne '*' -and $e.Current.Name -notlike $Filter) { continue }
        $r = $e.Current.BoundingRectangle
        '{0} | {1} | id={2} | {3},{4},{5},{6} | off={7} on={8}' -f $e.Current.ControlType.ProgrammaticName,$e.Current.Name,$e.Current.AutomationId,$r.X,$r.Y,$r.Width,$r.Height,$e.Current.IsOffscreen,$e.Current.IsEnabled
    }
    exit 0
}

# ============================ FULL RUN ============================
Log '=== REALTEST-001 Full run starting ==='
Log ('DPI awareness: per-monitor-v2 request=' + $script:DpiAware + ' error=' + $script:DpiAwareError + ' (PowerShell is DPI-unaware by default)')
$mons = @([RT]::Monitors())
Log ('Monitors: ' + (($mons | ForEach-Object { $_.Device + ' ' + $_.DeviceName + ' ' + $_.W + 'x' + $_.H + ' work=' + $_.WW + 'x' + $_.WH + ' dpi=' + $_.Dpi + ' primary=' + $_.Primary }) -join '; '))
$primary = @($mons | Where-Object Primary)[0]
if (-not $primary) { $primary = $mons[0] }
$second = @($mons | Where-Object { $_.Device -ne $primary.Device })[0]

# Full run mutates persisted Focus rules and workspaces, so a pre-existing FlowDesk is never used or closed.
$pre = @(Get-FlowDeskProcesses)
if ($pre.Count -gt 0) {
    Log ('BLOCKED: FlowDesk is already running (pid=' + (($pre | ForEach-Object { $_.Id }) -join ',') + '); close it before running Full')
    exit 2
}
$script:FlowDeskBaselinePids = @()
$app = $null
$startedApp = $true
Log 'FlowDesk not running; starting build exe.'
Start-Process -FilePath $AppExe | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 30) { $app = Get-AppProcess; if ($app -and $app.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 500 }
if (-not $app) { Log 'BLOCKED: FlowDesk.App did not start'; exit 2 }
if ($startedApp) { Register-FlowDeskOwned @(Get-FlowDeskProcesses | ForEach-Object { [uint32]$_.Id }) }
$fdPids = @(Get-FlowDeskProcesses | ForEach-Object { [uint32]$_.Id })
Log ('FlowDesk pid=' + $app.Id + ' hwnd=0x' + ('{0:X}' -f $app.MainWindowHandle) + ' startedByTest=' + $startedApp + ' allFlowDeskPids=' + ($fdPids -join ','))
Navigate 'Workspaces'

# Preflight cleanup of prior RT workspaces is allowed only for the three test names.
$existing = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.Name -in @('RT Work A','RT Work B','RT Layout') -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name })
if ($existing.Count -gt 0) {
    Log ('Prior test workspaces present: ' + ($existing -join ',') + ' - deleting them via FlowDesk UI first.')
    foreach ($name in $existing) {
        $act = Get-WorkspaceActionsButton $name
        if ($act) {
            Invoke-Element $act; Start-Sleep -Milliseconds 700
            $del = @(Get-ByName 'Delete' ([System.Windows.Automation.ControlType]::MenuItem) -Desktop)[0]
            if (-not $del) { $del = @(Get-ByName 'Delete' ([System.Windows.Automation.ControlType]::MenuItem))[0] }
            if ($del) {
                Invoke-Element $del; Start-Sleep -Milliseconds 700
                $dlg = @(Get-ByName 'Delete workspace?' ([System.Windows.Automation.ControlType]::Window))[0]
                if ($dlg) {
                    $confirm = @($dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq 'Delete workspace' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button })[0]
                    if ($confirm) { Invoke-Element $confirm }
                }
            }
        }
        Start-Sleep -Seconds 1
    }
    Navigate 'Workspaces'
}

# Pick up a bootstrap/child process the launcher may have spawned after the first scan.
if ($startedApp) {
    Register-FlowDeskOwned @(Get-FlowDeskProcesses | ForEach-Object { [uint32]$_.Id })
    $fdPids = @(Get-FlowDeskProcesses | ForEach-Object { [uint32]$_.Id })
}
# Baseline user windows BEFORE anything is launched.
$user0 = Snapshot-UserWindows @() @($fdPids)
$baselineHandles = @($user0.Keys)
Log ('User baseline windows: ' + $user0.Count)
foreach ($k in $user0.Keys) { Log ('USER 0x{0:X} {1} | {2} | {3},{4} {5}x{6} min={7} max={8}' -f $k,$user0[$k].Process,$user0[$k].Title,$user0[$k].X,$user0[$k].Y,$user0[$k].W,$user0[$k].H,$user0[$k].Min,$user0[$k].Max) }
$fg0 = [RT]::Window([RT]::Foreground()); Log ('Foreground at start: ' + $(if($fg0){$fg0.Title}else{'none'}))

# ---- launch disposable apps ----
$notepadA = Launch-Notepad $NotesA 'Notepad-A'
$paintA = Launch-Paint
$calc = Launch-Calculator
$explorerA = Launch-Explorer $FolderA 'Explorer-A'
$chrome = Launch-Chrome
Log ('Launched: notepad=' + $(if($notepadA){$notepadA.Handle}else{'FAIL'}) + ' paint=' + $(if($paintA){$paintA.Handle}else{'FAIL'}) + ' calc=' + $(if($calc){$calc.Handle}else{'SKIP'}) + ' explorer=' + $(if($explorerA){$explorerA.Handle}else{'FAIL'}) + ' chrome=' + $(if($chrome){$chrome.Handle}else{'FAIL'}))

# ---- arrange known rects ----
$areas = @()
$areas += [pscustomobject]@{ X=80;  Y=120; W=600; H=420 }
$areas += [pscustomobject]@{ X=720; Y=120; W=640; H=460 }
$areas += [pscustomobject]@{ X=1400;Y=120; W=380; H=520 }
$areas += [pscustomobject]@{ X=80;  Y=600; W=640; H=420 }
if ($second) { $areas += [pscustomobject]@{ X=($second.WX + 80); Y=($second.WY + 80); W=800; H=600 } }
else { $areas += [pscustomobject]@{ X=780; Y=620; W=620; H=380 } }
$targets = @()
if ($notepadA)  { $targets += [pscustomobject]@{ Name='Notepad'; Win=$notepadA } }
if ($paintA)    { $targets += [pscustomobject]@{ Name='Paint'; Win=$paintA } }
if ($calc)      { $targets += [pscustomobject]@{ Name='Calculator'; Win=$calc } }
if ($explorerA) { $targets += [pscustomobject]@{ Name='folder-a'; Win=$explorerA } }
if ($chrome)      { $targets += [pscustomobject]@{ Name='Chrome'; Win=$chrome } }
for ($i = 0; $i -lt $targets.Count; $i++) {
    $t = $targets[$i]; $a = $areas[$i]
    [void][RT]::SetPos([int64]$t.Win.Handle, $a.X, $a.Y, $a.W, $a.H)
    Start-Sleep -Milliseconds 350
    $t | Add-Member -NotePropertyName Rect -NotePropertyValue (Rect-Delta ([RT]::Window([int64]$t.Win.Handle)) $a.X $a.Y $a.W $a.H) -Force
}
$maxTarget = $targets | Where-Object Name -eq 'Paint' | Select-Object -First 1
if ($maxTarget) { [RT]::Show([int64]$maxTarget.Win.Handle, 3); Start-Sleep -Milliseconds 700 }
$expected = [ordered]@{}
foreach ($t in $targets) {
    $w = [RT]::Window([int64]$t.Win.Handle)
    $appPid = Get-AppProcessId $w; $appPi = Get-ProcInfo $appPid
    $expected[$t.Name] = [pscustomobject]@{
        Handle = [int64]$w.Handle; Pid = $w.Pid; AppPid = [uint32]$appPid; Process = $appPi.Name; Path = $appPi.Path
        Title = $w.Title; Class = $w.Class; Device = $w.Device
        X = $w.NormalX; Y = $w.NormalY; W = $w.NormalW; H = $w.NormalH
        Show = $w.Show; Min = $w.Min; Max = $w.Max; Monitor = (Monitor-IndexOfNormal $w $mons)
    }
    Log ('ARRANGE ' + $t.Name + ': ' + (Format-Window $w) + ' appPid=' + $appPid + ' process=' + $appPi.Name + ' monitor=' + $expected[$t.Name].Monitor)
}
$fg = [RT]::Window([RT]::Foreground()); Log ('Foreground before Save UI: ' + $(if($fg){$fg.Title}else{'none'}))
# FlowDesk names capture items from the executable's FileDescription, so Chrome is saved as "Google Chrome"
# (not "Chrome") and the details page shows that display name.
$displayFor = @{ 'Notepad' = 'Notepad'; 'Paint' = 'Paint'; 'Calculator' = 'Calculator'; 'folder-a' = 'folder-a'; 'Chrome' = 'Google Chrome' }
foreach ($t in $targets) { $t | Add-Member -NotePropertyName Display -NotePropertyValue $displayFor[$t.Name] -Force }
$expectedNames = @($targets | ForEach-Object Display)
$expectedCount = $targets.Count
Log ('Expected windows for RT Work A: ' + $expectedCount + ' [' + ($expectedNames -join ', ') + ']')
$launchMissing = @()
if (-not $notepadA) { $launchMissing += 'Notepad' }
if (-not $paintA) { $launchMissing += 'Paint' }
if (-not $explorerA) { $launchMissing += 'Explorer folder-a' }
if (-not $chrome) { $launchMissing += 'Chrome temp profile' }
if ($launchMissing.Count -gt 0) {
    Add-Result '0 Setup launches' 'BLOCKED' 'Notepad, Paint, Calculator (optional), Explorer folder-a, temp Chrome' ('missing: ' + ($launchMissing -join ', ') + '; Calculator=' + $(if($calc){'owned'}else{'skipped (ownership uncertain)'})) 'continuing with the windows that exist'
} elseif (-not $calc) {
    Add-Result '0 Setup launches' 'BLOCKED' 'Notepad, Paint, Calculator, Explorer folder-a, temp Chrome' ('Calculator skipped (ownership uncertain); other 4 launched') 'continuing with 4 windows'
} else {
    Log 'Setup launches: all 5 disposable windows owned.'
}

# ---- STEP 1: save RT Work A ----
$step = '1 Save RT Work A'
try {
    Navigate 'Workspaces'
    # The Save page snapshots windows when it opens; take the expected rects again right before that,
    # because browsers can change their own size after our arrange pass (stale expectations looked like a restore bug).
    Sync-ExpectedLive 'PRESAVE' $targets $expected $mons
    $newBtn = @(Get-ByName 'New workspace' ([System.Windows.Automation.ControlType]::SplitButton))[0]
    if (-not $newBtn) { $newBtn = @(Get-ByName 'New workspace' ([System.Windows.Automation.ControlType]::Button))[0] }
    if (-not $newBtn) { throw 'New workspace button not found' }
    Invoke-Element $newBtn
    $savePage = Wait-Ui { param() @(Get-ByName 'Save open windows' ([System.Windows.Automation.ControlType]::Text)).Count -gt 0 } 10
    if (-not $savePage) { throw 'Save open windows page did not open' }
    Start-Sleep -Milliseconds 800
    # Deselect everything (the page preselects all selectable windows, including the user's).
    $cbs = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::CheckBox })
    foreach ($cb in $cbs) { Set-Toggle $cb $false }
    Start-Sleep -Milliseconds 300
    $cbs = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::CheckBox })
    $details = @(Get-CandidateDetails $cbs)
    Log-Candidates $details
    $picked = New-Object System.Collections.ArrayList
    foreach ($t in $targets) {
        $handle = [int64]$expected[$t.Name].Handle
        $w = [RT]::Window($handle)
        $title = if ($w) { $w.Title } else { '' }
        # Exact ownership: the app exposes the captured window handle as AutomationId "window-<decimal>".
        $cand = Select-SaveCandidateById $details $handle $title $t.Name
        if (@($picked | Where-Object { [object]::ReferenceEquals($_, $cand) }).Count -gt 0) {
            throw ('BLOCKED: two workspace items resolved to the same Save candidate for ' + $t.Name)
        }
        Set-Toggle $cand $true
        [void]$picked.Add($cand)
    }
    Start-Sleep -Milliseconds 400
    $on = @($cbs | Where-Object { (Get-ToggleState $_) -eq 'On' })
    Log ('Save page selected ' + $on.Count + ' of ' + $cbs.Count + ' candidates: ' + (($on | ForEach-Object { $_.Current.Name }) -join ', '))
    if ($on.Count -ne $expectedCount) { throw ('Selected ' + $on.Count + ' but expected ' + $expectedCount) }
    $nameBox = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.Name -eq 'Workspace name' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit })[0]
    if (-not $nameBox) { throw 'Workspace name box not found' }
    $nameBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('RT Work A')
    Start-Sleep -Milliseconds 300
    $saveBtn = @(Get-ByName 'Save workspace' ([System.Windows.Automation.ControlType]::Button))[0]
    if (-not $saveBtn) { throw 'Save workspace button not found' }
    Invoke-Element $saveBtn
    $ok = Wait-Ui { param() @(Get-ByName 'Workspace details' ([System.Windows.Automation.ControlType]::Text)).Count -gt 0 } 10
    if (-not $ok) { throw 'Workspace details page did not appear after save' }
    Start-Sleep -Milliseconds 800
    $removes = @(Get-ByName 'Remove' ([System.Windows.Automation.ControlType]::Button))
    $itemTexts = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name })
    # Match each expected item to the display name FlowDesk actually shows (Chrome -> "Google Chrome");
    # keep the short test name in the report so a shortfall is attributable.
    $present = New-Object System.Collections.ArrayList
    $missing = New-Object System.Collections.ArrayList
    foreach ($t in $targets) {
        $hit = @($itemTexts | Where-Object { $_ -eq $t.Display })[0]
        if ($hit) { [void]$present.Add($t.Name + '=' + $hit) } else { [void]$missing.Add($t.Name) }
    }
    $detailNameBox = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and $_.Current.Name -eq 'Name' })[0]
    $savedName = ''
    if ($detailNameBox) { try { $savedName = $detailNameBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { } }
    # Accept the Name box value or the breadcrumb showing the workspace name.
    $nameOk = ($savedName -eq 'RT Work A')
    if (-not $nameOk) { $nameOk = @(Get-ByName 'RT Work A' | Where-Object { -not $_.Current.IsOffscreen }).Count -gt 0 }
    if ($savedName -eq '' -and $nameOk) { $savedName = 'RT Work A (breadcrumb)' }
    Log ('Details items: removedButtons=' + $removes.Count + ' matched=' + $present.Count + '/' + $expectedCount + ' name="' + $savedName + '"')
    $actual = ($removes.Count.ToString() + ' item cards; names: ' + (($present -join ', ') -replace '=([^,]+)', ' ($1)'))
    if ($missing.Count -gt 0) { $actual += '; missing: ' + ($missing -join ', ') }
    $actual += '; workspace name="' + $savedName + '"'
    if ($removes.Count -ne $expectedCount -or $missing.Count -gt 0 -or -not $nameOk) {
        Add-Result $step 'FAIL' ($expectedCount.ToString() + ' named item cards, workspace named RT Work A') $actual 'details page Remove buttons + item text'
        $null = Save-Shot 'step1-save-fail.png'
    } else {
        Add-Result $step 'PASS' ($expectedCount.ToString() + ' named item cards, workspace named RT Work A') $actual 'details page Remove buttons + item text'
    }
} catch {
    $msg = $_.Exception.Message
    if ($msg -like 'BLOCKED:*') {
        Add-Result $step 'BLOCKED' 'workspace RT Work A with exactly the owned windows' ($msg -replace '^BLOCKED: ','') 'candidate ownership was not proven'
    } else {
        Add-Result $step 'FAIL' 'workspace RT Work A with ' $msg 'see log'
    }
    $null = Save-Shot 'step1-save-exception.png'
}

# ---- STEP 2: close all, reopen RT Work A ----
$step = '2 Restore after close'
try {
    Close-AllApps
    Start-Sleep -Seconds 1
    $left = @(Get-RTWindows | Where-Object { $_.Handle -in (Get-OwnedHandles) })
    Log ('Owned windows still present after close: ' + $left.Count)
    Open-Workspace 'RT Work A'
    $summary = Wait-Report 'RT Work A' 75
    Log ('Restore summary: ' + $summary)
    $dialogTexts = Read-ReportDialog
    if ($dialogTexts.Count -gt 0) { Log ('Restore dialog: ' + ($dialogTexts -join ' || ')) }
    # give the settle pass time
    Start-Sleep -Seconds 2
    $now = Get-RTWindows
    $checks = @()
    foreach ($t in $targets) {
        $exp = $expected[$t.Name]
        $resolved = Resolve-RestoredWindow $t.Name $exp $now $baselineHandles
        $match = $resolved.Window
        if (-not $match) {
            Log ('RESTORE ' + $t.Name + ': ' + $resolved.Reason)
            $checks += [pscustomobject]@{ Name=$t.Name; Missing=$true; Delta=@(); Show='Missing'; ExpectedShow=$exp.Show; Line=($t.Name + ': MISSING (' + $resolved.Reason + ')') }
            continue
        }
        Add-Owned $t.Name $match $null | Out-Null
        Log-Window ('RESTORE ' + $t.Name) $match
        # Compare FlowDesk state (restore rect + show) instead of an icon rect or a stale arrange rect.
        $d = @([Math]::Abs($match.NormalX - $exp.X), [Math]::Abs($match.NormalY - $exp.Y), [Math]::Abs($match.NormalW - $exp.W), [Math]::Abs($match.NormalH - $exp.H))
        $checks += [pscustomobject]@{
            Name=$t.Name; Missing=$false; Delta=$d; Show=$match.Show; ExpectedShow=$exp.Show
            Line=($t.Name + ': delta=' + ($d -join ',') + ' show=' + $match.Show + '/' + $exp.Show + ' rect=' + $match.X + ',' + $match.Y + ' ' + $match.W + 'x' + $match.H)
        }
    }
    # Browsers may resize after the 1.2 s settle: this late read must also pass, so drift cannot hide.
    $chromeLate = ''
    $chromeLateOk = $true
    Start-Sleep -Seconds 3
    if ($expected.Contains('Chrome')) {
        try {
            $pids = @(Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object { $_.CommandLine -like ('*' + $ChromeProfile + '*') } | ForEach-Object { [uint32]$_.ProcessId })
            $cw = @(Get-RTWindows | Where-Object { $pids -contains $_.Pid -and $_.Class -eq 'Chrome_WidgetWin_1' })[0]
            if (-not $cw) {
                $chromeLate = 'lateWindow=MISSING'
                $chromeLateOk = $false
            } else {
                $exp = $expected['Chrome']
                $late = @([Math]::Abs($cw.NormalX-$exp.X),[Math]::Abs($cw.NormalY-$exp.Y),[Math]::Abs($cw.NormalW-$exp.W),[Math]::Abs($cw.NormalH-$exp.H))
                $chromeLate = 'lateDelta=' + ($late -join ',') + ' show=' + $cw.Show + '/' + $exp.Show
                $chromeLateOk = (@($late | Where-Object { $_ -gt 10 }).Count -eq 0 -and $cw.Show -eq $exp.Show)
                Log-Window 'CHROME LATE' $cw
                Log ('Chrome late drift check: ' + $chromeLate + ' ok=' + $chromeLateOk)
            }
        } catch { $chromeLate = 'lateCheck=ERROR'; $chromeLateOk = $false }
    }
    $summaryOk = $summary -match ('RT Work A: ' + $expectedCount + ' of ' + $expectedCount + ' windows in place')
    $badDeltas = @($checks | Where-Object { $_.Missing -or @($_.Delta | Where-Object { $_ -gt 10 }).Count -gt 0 })
    $showBad = @($checks | Where-Object { -not $_.Missing -and $_.Show -ne $_.ExpectedShow })
    $maximizedOk = @($checks | Where-Object { $_.Name -eq 'Paint' -and $_.Show -eq 'Maximized' }).Count -eq 1
    $lines = @($checks | ForEach-Object { $_.Line })
    $chromeNote = @($dialogTexts | Where-Object { $_ -like '*Chrome*' -or $_ -like '*browser*' }) -join ' / '
    $actual = $summary + ' | ' + ($lines -join '; ')
    if ($chromeLate) { $actual += ' | ' + $chromeLate }
    if ($summaryOk -and $badDeltas.Count -eq 0 -and $showBad.Count -eq 0 -and $maximizedOk -and $chromeLateOk) {
        Add-Result $step 'PASS' ($expectedCount.ToString() + ' windows back in their saved show state/rects (tolerance 10px), summary "N of N"') $actual ('dialog: ' + ($dialogTexts -join ' | '))
    } else {
        Add-Result $step 'FAIL' ($expectedCount.ToString() + ' windows back in their saved show state/rects') $actual ('dialog: ' + ($dialogTexts -join ' | '))
        $null = Save-Shot 'step2-restore-fail.png'
    }
    Log ('Chrome reopened note: ' + $chromeNote)
} catch {
    Add-Result $step 'FAIL' '5 windows restored' ($_.Exception.Message) 'see log'
    $null = Save-Shot 'step2-restore-exception.png'
}

# Capture A reference positions for step 3.
$aLive = @{}
foreach ($t in $targets) {
    $exp = $expected[$t.Name]; $w = [RT]::Window([int64]$exp.Handle)
    if (-not $w) {
        # The old handle is gone; re-resolve with the same ownership rules (baseline + executable + new process).
        $resolved = Resolve-RestoredWindow $t.Name $exp (Get-RTWindows) $baselineHandles
        $w = $resolved.Window
        if (-not $w) { Log ('A live ' + $t.Name + ': ' + $resolved.Reason) }
    }
    if ($w) {
        $aLive[$t.Name] = [pscustomobject]@{
            Handle=[int64]$w.Handle; Pid=$w.Pid; Title=$w.Title; Class=$w.Class
            X=$w.NormalX; Y=$w.NormalY; W=$w.NormalW; H=$w.NormalH; Show=$w.Show; Min=$w.Min; Max=$w.Max
            VisibleX=$w.X; VisibleY=$w.Y; VisibleW=$w.W; VisibleH=$w.H; Device=$w.Device
        }
        Log-Window ('A live ' + $t.Name) $w
    }
}
Log ('A live snapshot: ' + (($aLive.Keys | ForEach-Object { $_ + '=0x' + ('{0:X}' -f $aLive[$_].Handle) + '/' + $aLive[$_].Show }) -join ', '))

# ---- STEP 3: B and A -> B -> A ----
$step = '3 Switch A -> B -> A'
try {
    # B windows: Notepad notes-b, second Paint, Explorer folder-b
    $notepadB = Launch-Notepad $NotesB 'Notepad-B'
    $paintB = Launch-PaintB
    $explorerB = Launch-Explorer $FolderB 'Explorer-B'
    $bWins = @()
    if ($notepadB) { $bWins += [pscustomobject]@{ Name='Notepad'; Win=$notepadB; X=1200; Y=200; W=560; H=400 } }
    if ($paintB)   { $bWins += [pscustomobject]@{ Name='Paint'; Win=$paintB; X=500; Y=300; W=520; H=600 } }
    if ($explorerB){ $bWins += [pscustomobject]@{ Name='folder-b'; Win=$explorerB; X=100; Y=250; W=620; H=460 } }
    foreach ($b in $bWins) { [void][RT]::SetPos([int64]$b.Win.Handle, $b.X, $b.Y, $b.W, $b.H); Start-Sleep -Milliseconds 300 }
    if ($paintB) { [RT]::Top([int64]$paintB.Handle) }
    Start-Sleep -Milliseconds 400
    $bExpected = @{}
    foreach ($b in $bWins) {
        $w = [RT]::Window([int64]$b.Win.Handle)
        $bExpected[$b.Name] = [pscustomobject]@{
            Handle=[int64]$w.Handle; Pid=$w.Pid; Title=$w.Title; Class=$w.Class; Device=$w.Device
            X=$w.NormalX; Y=$w.NormalY; W=$w.NormalW; H=$w.NormalH; Show=$w.Show; Min=$w.Min; Max=$w.Max
        }
        Log-Window ('B expected ' + $b.Name) $w
    }
    Log ('B expected: ' + (($bWins | ForEach-Object { $_.Name + '=' + $bExpected[$_.Name].X + ',' + $bExpected[$_.Name].Y + ' ' + $bExpected[$_.Name].W + 'x' + $bExpected[$_.Name].H + ' show=' + $bExpected[$_.Name].Show }) -join '; '))
    Navigate 'Workspaces'
    $newBtn = @(Get-ByName 'New workspace' ([System.Windows.Automation.ControlType]::SplitButton))[0]
    if (-not $newBtn) { $newBtn = @(Get-ByName 'New workspace' ([System.Windows.Automation.ControlType]::Button))[0] }
    Invoke-Element $newBtn
    $null = Wait-Ui { param() @(Get-ByName 'Save open windows' ([System.Windows.Automation.ControlType]::Text)).Count -gt 0 } 10
    Start-Sleep -Milliseconds 800
    $cbs = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::CheckBox })
    foreach ($cb in $cbs) { Set-Toggle $cb $false }
    Start-Sleep -Milliseconds 300
    $cbs = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::CheckBox })
    $bDetails = @(Get-CandidateDetails $cbs)
    Log-Candidates $bDetails
    foreach ($b in $bWins) {
        $w = [RT]::Window([int64]$b.Win.Handle)
        $title = if ($w) { $w.Title } else { '' }
        # Exact handle from this launch, not title order: A and B both have "Untitled - Paint".
        $cand = Select-SaveCandidateById $bDetails ([int64]$b.Win.Handle) $title $b.Name
        Set-Toggle $cand $true
    }
    $on = @($cbs | Where-Object { (Get-ToggleState $_) -eq 'On' })
    Log ('B selection: ' + $on.Count + ' [' + (($on | ForEach-Object { $_.Current.Name }) -join ',') + ']')
    if ($on.Count -ne $bWins.Count) { throw ('B selected ' + $on.Count + ' expected ' + $bWins.Count) }
    $nameBox = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.Name -eq 'Workspace name' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit })[0]
    $nameBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('RT Work B')
    Start-Sleep -Milliseconds 300
    Invoke-Element @(Get-ByName 'Save workspace' ([System.Windows.Automation.ControlType]::Button))[0]
    $null = Wait-Ui { param() @(Get-ByName 'Workspace details' ([System.Windows.Automation.ControlType]::Text)).Count -gt 0 } 10
    Start-Sleep -Milliseconds 700
    $bRemoves = @(Get-ByName 'Remove' ([System.Windows.Automation.ControlType]::Button)).Count
    Log ('B details item cards: ' + $bRemoves)
    # read minimize previous setting
    Navigate 'Settings'
    Start-Sleep -Milliseconds 500
    $minToggle = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.Name -eq 'Minimize previous workspace windows' })[0]
    $minState = if ($minToggle) { Get-ToggleState $minToggle } else { 'Unknown' }
    Log ('Minimize previous setting: ' + $minState)
    # Open A, then B, then A; keep the matching Paint instance on top so the matcher never steals the other one.
    if ($aLive.ContainsKey('Paint')) { [RT]::Top([int64]$aLive['Paint'].Handle) }
    Open-Workspace 'RT Work A'; $sumA1 = Wait-Report 'RT Work A' 75
    Start-Sleep -Seconds 1
    if ($paintB) { [RT]::Top([int64]$paintB.Handle) }
    Open-Workspace 'RT Work B'; $sumB = Wait-Report 'RT Work B' 75
    Start-Sleep -Seconds 2
    $bLive = @{}
    foreach ($b in $bWins) { $w = [RT]::Window([int64]$b.Win.Handle); if ($w) { $bLive[$b.Name] = $w } }
    if ($aLive.ContainsKey('Paint')) { [RT]::Top([int64]$aLive['Paint'].Handle) }
    Open-Workspace 'RT Work A'; $sumA2 = Wait-Report 'RT Work A' 75
    Start-Sleep -Seconds 2
    $aBack = @{}
    $aMinimizedAfter = @()
    foreach ($k in $aLive.Keys) {
        $w = [RT]::Window([int64]$aLive[$k].Handle)
        if ($w) { $aBack[$k] = $w; if ($w.Min) { $aMinimizedAfter += $k }; Log-Window ('A back ' + $k) $w }
        else { Log ('A back ' + $k + ': window 0x' + ('{0:X}' -f $aLive[$k].Handle) + ' is gone') }
    }
    $bMinimized = @()
    foreach ($k in $bLive.Keys) {
        $cur = [RT]::Window([int64]$bLive[$k].Handle)
        if ($cur) { Log-Window ('B after ' + $k) $cur; if ($cur.Min) { $bMinimized += $k } }
    }
    # Compare the state FlowDesk controls (restore rect + show), not the -32000 minimized icon rect.
    $aStates = @()
    foreach ($k in $aLive.Keys) {
        if (-not $aBack.ContainsKey($k)) { $aStates += [pscustomobject]@{ Name=$k; Missing=$true; RectOk=$false; ShowOk=$false; Line=($k+': MISSING after B->A') }; continue }
        $e2 = $aLive[$k]; $w = $aBack[$k]
        $d = @([Math]::Abs($w.NormalX-$e2.X),[Math]::Abs($w.NormalY-$e2.Y),[Math]::Abs($w.NormalW-$e2.W),[Math]::Abs($w.NormalH-$e2.H))
        $aStates += [pscustomobject]@{ Name=$k; Missing=$false; Delta=$d; Show=$w.Show; ExpectedShow=$e2.Show
            RectOk=(@($d | Where-Object { $_ -gt 10 }).Count -eq 0); ShowOk=($w.Show -eq $e2.Show)
            Line=($k+': normalDelta=' + ($d -join ',') + ' show=' + $e2.Show + '->' + $w.Show) }
    }
    $bStates = @()
    $expectBMin = if ($minState -eq 'On') { $bWins.Count } else { 0 }
    foreach ($k in $bLive.Keys) {
        $cur = [RT]::Window([int64]$bLive[$k].Handle)
        if (-not $cur -or -not $bExpected.ContainsKey($k)) { $bStates += [pscustomobject]@{ Name=$k; Missing=$true; RectOk=$false; ShowOk=$false; Show='Missing'; ExpectedShow=''; Line=($k+': MISSING after B->A') }; continue }
        $e2 = $bExpected[$k]
        $d = @([Math]::Abs($cur.NormalX-$e2.X),[Math]::Abs($cur.NormalY-$e2.Y),[Math]::Abs($cur.NormalW-$e2.W),[Math]::Abs($cur.NormalH-$e2.H))
        $wantShow = if ($minState -eq 'On') { 'Minimized' } else { $e2.Show }
        $bStates += [pscustomobject]@{ Name=$k; Missing=$false; Delta=$d; Show=$cur.Show; ExpectedShow=$wantShow
            RectOk=(@($d | Where-Object { $_ -gt 10 }).Count -eq 0); ShowOk=($cur.Show -eq $wantShow)
            Line=($k+': normalDelta=' + ($d -join ',') + ' show=' + $cur.Show + '/' + $wantShow) }
    }
    $issues = Compare-UserWindows $user0 (Get-OwnedHandles) $fdPids
    $badA = @($aStates | Where-Object { -not $_.RectOk -or -not $_.ShowOk })
    $badB = @($bStates | Where-Object { -not $_.RectOk -or -not $_.ShowOk })
    Log ('A->B->A: A minimized after = [' + ($aMinimizedAfter -join ',') + '] B minimized = [' + ($bMinimized -join ',') + '] userIssues=' + $issues.Count)
    Log ('A states: ' + (($aStates | ForEach-Object { $_.Line }) -join '; '))
    Log ('B states: ' + (($bStates | ForEach-Object { $_.Line }) -join '; '))
    $sumOk = ($sumA1 -match ('RT Work A: ' + $expectedCount + ' of ' + $expectedCount)) -and ($sumB -match ('RT Work B: ' + $bWins.Count + ' of ' + $bWins.Count)) -and ($sumA2 -match ('RT Work A: ' + $expectedCount + ' of ' + $expectedCount))
    $ok = $sumOk -and ($badA.Count -eq 0) -and ($badB.Count -eq 0) -and ($aMinimizedAfter.Count -eq 0) -and ($bMinimized.Count -eq $expectBMin) -and ($issues.Count -eq 0)
    $actual = ('A minimized=' + $aMinimizedAfter.Count + '; B minimized=' + $bMinimized.Count + '/' + $expectBMin + ' (setting ' + $minState + '); A states ' + (($aStates | ForEach-Object { $_.Line }) -join ',') + '; B states ' + (($bStates | ForEach-Object { $_.Line }) -join ',') + '; user changes=' + $issues.Count + '; summaries: ' + $sumA1 + ' / ' + $sumB + ' / ' + $sumA2)
    if ($ok) { Add-Result $step 'PASS' ('A back at its rects/show state, B minimized per setting, no user window changed') $actual ('B item cards=' + $bRemoves) }
    else { Add-Result $step 'FAIL' ('A back at its rects/show state, B minimized per setting, no user window changed') $actual (($issues -join '; ') + ' | ' + (($badA | ForEach-Object { $_.Line }) -join '; ') + ' | ' + (($badB | ForEach-Object { $_.Line }) -join '; ')); $null = Save-Shot 'step3-switch-fail.png' }
} catch {
    $msg = $_.Exception.Message
    if ($msg -like 'BLOCKED:*') {
        Add-Result $step 'BLOCKED' 'A -> B -> A with exactly the owned windows' ($msg -replace '^BLOCKED: ','') 'candidate ownership was not proven'
    } else {
        Add-Result $step 'FAIL' 'A -> B -> A' $msg 'see log'
    }
    $null = Save-Shot 'step3-switch-exception.png'
}

# ---- STEP 4: layouts ----
$step = '4 Layouts two columns'
try {
    Open-Workspace 'RT Work A' | Out-Null
    $null = Wait-Report 'RT Work A' 60
    Navigate 'Layouts'
    $refresh = @(Get-ByName 'Refresh' ([System.Windows.Automation.ControlType]::Button))[0]
    if ($refresh) { Invoke-Element $refresh }
    Start-Sleep -Seconds 2
    $twoCol = @(Get-ByName 'Two columns' ([System.Windows.Automation.ControlType]::Text))[0]
    if (-not $twoCol) { $twoCol = @(Get-ByContains 'Two columns' ([System.Windows.Automation.ControlType]::Text))[0] }
    if (-not $twoCol) { throw 'Two columns preset not found' }
    $sel = Find-Ancestor $twoCol { param($n) $p=$null; $n.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$p) }
    if (-not $sel) { throw 'Two columns item is not selectable' }
    Select-Element $sel
    Start-Sleep -Milliseconds 400
    $apply = @(Get-ByName 'Apply layout' ([System.Windows.Automation.ControlType]::Button))[0]
    if (-not $apply) { throw 'Apply layout button not found' }
    Invoke-Element $apply
    Start-Sleep -Seconds 3
    $statusTexts = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name })
    $placed = @($statusTexts | Where-Object { $_ -like 'Placed * windows*' })[0]
    $labels = @($statusTexts | Where-Object { $_ -like 'Display *' -or $_ -like '* × *' })
    $winList = @(Get-ByName 'Windows in layout order' ([System.Windows.Automation.ControlType]::List))[0]
    $winCount = 0; $listOrder = @()
    if ($winList) {
        $items = @($winList.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition))
        $winCount = $items.Count
        $listOrder = @($items | ForEach-Object { $_.Current.Name })
    }
    $shot = Save-Shot 'step4-layouts.png'
    # Read the monitor the user/app actually selected in the Layouts page (RadioButton "Display N ..."):
    # core LayoutApplier uses that device, not necessarily the first window's monitor.
    $monRadios = @(Get-UiAll (Get-AppRoot) | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::RadioButton -and $_.Current.Name -like 'Display *'
    })
    foreach ($r in $monRadios) { Log ('MONITOR choice "' + $r.Current.Name + '" selected=' + (Get-SelectedState $r)) }
    $selRadio = @($monRadios | Where-Object { (Get-SelectedState $_) -eq $true })[0]
    if (-not $selRadio) { throw 'BLOCKED: layout monitor selection is not readable; cannot say which single monitor was chosen' }
    $mLabel = $selRadio.Current.Name
    if ($mLabel -notmatch '^Display\s+(\d+)') { throw ('BLOCKED: layout monitor label "' + $mLabel + '" has no DISPLAY number') }
    $chosenDevice = '\\.\DISPLAY' + $matches[1]
    $chosenIdx = Monitor-IndexByDevice $chosenDevice $mons
    if ($chosenIdx -lt 0) {
        throw ('BLOCKED: layout chose "' + $mLabel + '" (' + $chosenDevice + ') but the test process sees [' + (($mons | ForEach-Object { $_.DeviceName }) -join ', ') + ']')
    }
    Log ('Layout monitor labels: ' + ($labels -join ' | ') + ' ; chosen=' + $mLabel + ' (' + $chosenDevice + ') idx=' + $chosenIdx + ' ; placed=' + $placed + ' ; listItems=' + $winCount + ' [' + ($listOrder -join ' | ') + '] ; shot=' + $shot)
    # Check every live A window by its FlowDesk normal rect: a minimized UWP window reports -32000 icon rect,
    # which must not be mistaken for a second monitor.
    $now = Get-RTWindows
    $aNow = @()
    foreach ($k in $aLive.Keys) { $w = [RT]::Window([int64]$aLive[$k].Handle); if ($w) { $aNow += $w; Log-Window ('LAYOUT ' + $k) $w } }
    $monIdx = @($aNow | ForEach-Object { Monitor-IndexOfNormal $_ $mons } | Select-Object -Unique)
    $offMonitor = @($aNow | Where-Object { (Monitor-IndexOfNormal $_ $mons) -ne $chosenIdx })
    $minimizedAfter = @($aNow | Where-Object { $_.Min })
    $tiled = $placed -match 'Placed (\d+) of (\d+)' 
    $placedCount = 0; $totalCount = $aNow.Count
    if ($tiled) { $placedCount = [int]$matches[1]; $totalCount = [int]$matches[2] }
    $boundsOk = ($offMonitor.Count -eq 0)
    $shownOk = ($minimizedAfter.Count -eq 0)
    # save layout
    $savePos = @(Get-ByName 'Save as workspace positions' ([System.Windows.Automation.ControlType]::Button))[0]
    $savedMsg = ''
    if ($savePos -and $savePos.Current.IsEnabled) { Invoke-Element $savePos; Start-Sleep -Seconds 2; $savedMsg = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -like 'Saved positions*' } | ForEach-Object { $_.Current.Name })[0] }
    # move one window, then restore layout
    $move = @($aNow | Where-Object { $_.Title -like '*notes-a*' })[0]
    if (-not $move) { $move = $aNow[0] }
    $mvX = $move.NormalX + 140; $mvY = $move.NormalY + 90
    [void][RT]::SetPos([int64]$move.Handle, $mvX, $mvY, $move.NormalW, $move.NormalH)
    Start-Sleep -Milliseconds 700
    $afterMove = [RT]::Window([int64]$move.Handle)
    $moveDelta = @([Math]::Abs($afterMove.NormalX-$move.NormalX),[Math]::Abs($afterMove.NormalY-$move.NormalY))
    $moveOk = (@($moveDelta | Where-Object { $_ -gt 10 }).Count -gt 0)
    Invoke-Element $apply
    Start-Sleep -Seconds 3
    $afterRestore = [RT]::Window([int64]$move.Handle)
    $restoreDelta = @([Math]::Abs($afterRestore.NormalX-$move.NormalX),[Math]::Abs($afterRestore.NormalY-$move.NormalY),[Math]::Abs($afterRestore.NormalW-$move.NormalW),[Math]::Abs($afterRestore.NormalH-$move.NormalH))
    $countOk = ($totalCount -eq $expectedCount -and $aNow.Count -eq $expectedCount)
    $stateText = 'chosen=' + $mLabel + ' idx=' + $chosenIdx + '; monitors=' + ($monIdx -join ',') + '; count=' + $aNow.Count + '/' + $expectedCount + '; offMonitor=[' + (($offMonitor | ForEach-Object { $_.Title }) -join ',') + ']; minimizedAfter=[' + (($minimizedAfter | ForEach-Object { $_.Title }) -join ',') + ']; moveDelta=' + ($moveDelta -join ',')
    Log ('Layout save: ' + $savedMsg + ' ; tiled=' + $placedCount + '/' + $totalCount + ' ' + $stateText + ' restoreDelta=' + ($restoreDelta -join ','))
    if ($tiled -and $placedCount -eq $totalCount -and $countOk -and $boundsOk -and $shownOk -and $moveOk -and ($savedMsg -ne '') -and (@($restoreDelta | Where-Object { $_ -gt 10 }).Count -eq 0)) {
        Add-Result $step 'PASS' ('all ' + $expectedCount + ' windows tiled on the chosen monitor, positions saved, restore returns the moved window') ('placed ' + $placedCount + '/' + $totalCount + '; ' + $stateText + '; saved="' + $savedMsg + '"; restore delta=' + ($restoreDelta -join ',') + '; labels=' + $(if($labels.Count){$labels -join ' | '}else{'none'})) ('screenshot ' + $shot)
    } else {
        $null = Save-Shot 'step4-layouts-fail.png'
        Add-Result $step 'FAIL' ('all ' + $expectedCount + ' windows tiled on the chosen monitor, positions saved, restore returns the moved window') ('placed ' + $placedCount + '/' + $totalCount + '; ' + $stateText + '; saved="' + $savedMsg + '"; restore delta=' + ($restoreDelta -join ',') + '; labels=' + $(if($labels.Count){$labels -join ' | '}else{'none'})) ('screenshot ' + $shot)
    }
} catch {
    $msg = $_.Exception.Message
    if ($msg -like 'BLOCKED:*') {
        Add-Result $step 'BLOCKED' 'Two columns layout applied on one known monitor' ($msg -replace '^BLOCKED: ','') 'monitor identity not provable'
    } else {
        Add-Result $step 'FAIL' 'Two columns layout applied, saved and restored' $msg 'see log'
    }
    $null = Save-Shot 'step4-layouts-exception.png'
}

# ---- STEP 5: focus mode ----
$step = '5 Focus Normal and Gentle'
try {
    # Safety: no user Notepad windows, and BlockAll must be off.
    $userNow = Snapshot-UserWindows (Get-OwnedHandles) $fdPids
    $userNotepad = @($userNow.GetEnumerator() | Where-Object { $_.Value.Process -match '^(?i)notepad$' } | ForEach-Object { $_.Value.Title })
    Navigate 'Focus'
    Start-Sleep -Milliseconds 700
    $blockAll = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.Name -like 'Block every app*' -and (Get-ToggleState $_) -ne 'Unknown' })[0]
    $blockAllState = if ($blockAll) { Get-ToggleState $blockAll } else { 'Unknown' }
    $existingBlocked = @(Get-FocusRuleRows 'Remove blocked app')
    foreach ($rule in $existingBlocked) { Log ('Focus blocked rule: ' + $rule) }
    $foreignBlocked = @($existingBlocked | Where-Object { $_ -notmatch '^(?i)Notepad( \| (notepad\.exe|Packaged app))?$' })
    Log ('Focus preflight: userNotepad=' + $userNotepad.Count + ' blockAll=' + $blockAllState + ' blockedRules=' + $existingBlocked.Count + ' foreign=' + $foreignBlocked.Count)
    if ($userNotepad.Count -gt 0) { throw ('BLOCKED: user Notepad window present: ' + ($userNotepad -join ', ')) }
    if ($blockAllState -eq 'On') { throw 'BLOCKED: "Block every app" is On; a session could close user windows' }
    if ($foreignBlocked.Count -gt 0) { throw ('BLOCKED: existing focus rules other than Notepad could close user apps: ' + ($foreignBlocked -join '; ')) }
    # Ensure notepad is on the blocked list (add it while our Notepad A is still open).
    $blockedTexts = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name })
    $addedRule = $false
    if (-not ($blockedTexts -contains 'Notepad' -or $blockedTexts -contains 'notepad.exe')) {
        # The Focus UI adds a blocked app from a running app or a Browse dialog; there is no free-text
        # enter field. Make sure a test-owned Notepad is running (retry once; packaged Notepad can be slow).
        $np = @(Get-RTWindows | Where-Object { $_.Title -like '*notes-a.txt*' -or $_.Title -like '*notes-b.txt*' })[0]
        if (-not $np) { $np = Launch-Notepad $NotesA 'Notepad-FocusRule' }
        if (-not $np) { Start-Sleep -Seconds 2; $np = Launch-Notepad $NotesA 'Notepad-FocusRule' }
        if (-not $np) { throw 'BLOCKED: could not launch a test-owned Notepad for the blocked rule' }
        $addBtn = @(Get-ByName 'Add blocked app' ([System.Windows.Automation.ControlType]::Button))[0]
        if (-not $addBtn) { $addBtn = @(Get-ByName 'Add blocked app' ([System.Windows.Automation.ControlType]::SplitButton))[0] }
        if (-not $addBtn) { throw 'Add blocked app button not found' }
        $pat = $null
        if ($addBtn.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$pat)) { $pat.Expand() } else { Invoke-Element $addBtn }
        Start-Sleep -Milliseconds 700
        $runItem = @(Get-ByName 'Running app' ([System.Windows.Automation.ControlType]::MenuItem) -Desktop)[0]
        if (-not $runItem) { $runItem = @(Get-ByName 'Running app' ([System.Windows.Automation.ControlType]::MenuItem))[0] }
        if (-not $runItem) { throw 'Running app menu item not found' }
        Invoke-Element $runItem
        Start-Sleep -Seconds 1
        $pick = @(Get-ByName 'Pick a running app' ([System.Windows.Automation.ControlType]::Window))[0]
        if (-not $pick) { throw 'Pick a running app dialog not found' }
        $npText = @($pick.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq 'Notepad' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text })[0]
        if (-not $npText) { $npText = @($pick.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like '*Notepad*' })[0] }
        if (-not $npText) { throw 'Notepad row not found in Pick a running app' }
        $li = Find-Ancestor $npText { param($n) $p=$null; $n.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$p) }
        if (-not $li) { throw 'Notepad row is not selectable' }
        Select-Element $li
        Start-Sleep -Milliseconds 400
        $addApp = @($pick.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq 'Add app' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button })[0]
        if (-not $addApp) { throw 'Add app button not found' }
        Invoke-Element $addApp
        Start-Sleep -Seconds 1
        $ruleTexts = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name })
        if (-not ($ruleTexts -contains 'Notepad' -or $ruleTexts -contains 'notepad.exe')) { throw 'BLOCKED: Notepad blocked rule did not appear after Add' }
        $addedRule = $true
        Log 'Added Notepad to blocked apps.'
    } else { Log 'Notepad already on blocked apps list; will not remove it.' }
    # Our own Notepad windows would otherwise be closed by the session the moment it starts.
    # Close only test-owned/fresh fixture windows, never a user instance that happens to share a title.
    Close-FixtureWindows 'focus pre-session'
    Start-Sleep -Seconds 1
    # Re-check just before enforcement starts: a notepad.exe block is global, so no user Notepad may be up.
    $userNow2 = Snapshot-UserWindows (Get-OwnedHandles) $fdPids
    $userNotepad2 = @($userNow2.GetEnumerator() | Where-Object { $_.Value.Process -match '^(?i)notepad$' } | ForEach-Object { $_.Value.Title })
    if ($userNotepad2.Count -gt 0) { throw ('BLOCKED: user Notepad window appeared: ' + ($userNotepad2 -join ', ')) }
    $beforeStartBlocked = @(Get-FocusRuleRows 'Remove blocked app')
    $foreignBeforeStart = @($beforeStartBlocked | Where-Object { $_ -notmatch '^(?i)Notepad( \| (notepad\.exe|Packaged app))?$' })
    if ($foreignBeforeStart.Count -gt 0) { throw ('BLOCKED: focus rules changed and now include non-Notepad apps: ' + ($foreignBeforeStart -join '; ')) }
    # Normal, 25 minutes.
    $normal = @(Get-ByName 'Normal' ([System.Windows.Automation.ControlType]::RadioButton))[0]
    if ($normal) { Select-Element $normal }
    $d25 = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::RadioButton -and ($_.Current.Name -like '25*') })[0]
    if ($d25) { Select-Element $d25 }
    Start-Sleep -Milliseconds 500
    $startBtn = @(Get-ByName 'Start focus' ([System.Windows.Automation.ControlType]::Button))[0]
    if (-not $startBtn) { throw 'Start focus button not found' }
    Invoke-Element $startBtn
    $running = Wait-Ui { param() @(Get-ByName 'Session running' ([System.Windows.Automation.ControlType]::Text)).Count -gt 0 } 12
    if (-not $running) { throw 'Normal focus session did not start' }
    $rem = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -match '^\d\d:\d\d$' } | ForEach-Object { $_.Current.Name })[0]
    Log ('Normal session running, remaining=' + $rem + ' (expected 25:00)')
    $null = Launch-Notepad $NotesC 'Notepad-C'
    $npC = @(Get-RTWindows | Where-Object { $_.Title -like '*notes-c.txt*' })
    $seen = $npC.Count -gt 0
    $closed = Wait-NoWindow { param($x) $x.Title -like '*notes-c.txt*' } 12
    $toast = @(Get-DesktopAll | Where-Object { $_.Current.Name -like '*closed for this focus session*' -or $_.Current.Name -eq 'Allow 5 minutes' })
    $toastSeen = $toast.Count -gt 0
    Log ('Normal: notes-c window seen=' + $seen + ' closed=' + $closed + ' toastText=' + $toastSeen)
    $endBtn = @(Get-ByName 'End session' ([System.Windows.Automation.ControlType]::Button))[0]
    if ($endBtn) { Invoke-Element $endBtn }
    $idle = Wait-Ui { param() @(Get-ByName 'Session running' ([System.Windows.Automation.ControlType]::Text)).Count -eq 0 } 10
    Log ('Normal session ended: ' + $idle)
    # Gentle
    Navigate 'Focus'
    Start-Sleep -Milliseconds 600
    $gentle = @(Get-ByName 'Gentle' ([System.Windows.Automation.ControlType]::RadioButton))[0]
    if ($gentle) { Select-Element $gentle }
    Start-Sleep -Milliseconds 400
    $startBtn = @(Get-ByName 'Start focus' ([System.Windows.Automation.ControlType]::Button))[0]
    Invoke-Element $startBtn
    $running2 = Wait-Ui { param() @(Get-ByName 'Session running' ([System.Windows.Automation.ControlType]::Text)).Count -gt 0 } 12
    if (-not $running2) { throw 'Gentle focus session did not start' }
    $null = Launch-Notepad $NotesC2 'Notepad-C2'
    Start-Sleep -Seconds 4
    $npC2 = @(Get-RTWindows | Where-Object { $_.Title -like '*notes-c2.txt*' })
    $gentleStays = $npC2.Count -gt 0
    Log ('Gentle: notes-c2 stays=' + $gentleStays + ' windowCount=' + $npC2.Count)
    $endBtn = @(Get-ByName 'End session' ([System.Windows.Automation.ControlType]::Button))[0]
    if ($endBtn) { Invoke-Element $endBtn }
    $idle2 = Wait-Ui { param() @(Get-ByName 'Session running' ([System.Windows.Automation.ControlType]::Text)).Count -eq 0 } 10
    # remove our blocked rule again
    $removed = $false
    if ($addedRule) {
        Start-Sleep -Milliseconds 700
        $notepadRow = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -eq 'Notepad' })[0]
        if ($notepadRow) {
            $card = Find-Ancestor $notepadRow { param($n) $all=$n.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition); @($all | Where-Object { $_.Current.Name -eq 'Remove blocked app' }).Count -eq 1 }
            if ($card) { $rm = @($card.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq 'Remove blocked app' })[0]; if ($rm) { Invoke-Element $rm; $removed = $true } }
        }
    }
    Log ('Blocked rule removed=' + $removed)
    # close gentle notepad and any notes-c leftovers (ownership-checked by PID/freshness)
    Close-FixtureWindows 'focus after gentle'
    $okNorm = $closed -and $idle
    $okGentle = $gentleStays -and $idle2
    if ($okNorm -and $okGentle) {
        Add-Result $step 'PASS' 'Normal closes a newly launched Notepad; Gentle warns and leaves it open; both sessions end via UI' ('Normal: windowSeen=' + $seen + ' closed=' + $closed + ' toastActionSeen=' + $toastSeen + ' ended=' + $idle + '; Gentle: stayed=' + $gentleStays + ' ended=' + $idle2 + '; 25min remaining=' + $rem) ('blocked rule added=' + $addedRule + ' removed=' + $removed)
    } else {
        Add-Result $step 'FAIL' 'Normal closes, Gentle leaves open, both end' ('Normal: seen=' + $seen + ' closed=' + $closed + ' ended=' + $idle + '; Gentle: stayed=' + $gentleStays + ' ended=' + $idle2) ('toastSeen=' + $toastSeen + '; rule added=' + $addedRule + ' removed=' + $removed)
        $null = Save-Shot 'step5-focus-fail.png'
    }
} catch {
    $msg = $_.Exception.Message
    if ($msg -like 'BLOCKED:*') {
        Add-Result $step 'BLOCKED' 'Normal/Gentle focus behaviour' ($msg -replace '^BLOCKED: ','') 'session not started'
        # make sure no session was left running
        try { $e = @(Get-ByName 'End session' ([System.Windows.Automation.ControlType]::Button))[0]; if ($e) { Invoke-Element $e } } catch { }
    } else {
        Add-Result $step 'FAIL' 'Normal/Gentle focus behaviour' $msg 'see log'
        $null = Save-Shot 'step5-focus-exception.png'
    }
}

# ---- STEP 6: autosave ----
$step = '6 Autosave after app restart'
try {
    Open-Workspace 'RT Work A' | Out-Null
    $null = Wait-Report 'RT Work A' 60
    Start-Sleep -Seconds 1
    $move = @(Get-RTWindows | Where-Object { $_.Title -like '*folder-a*' })[0]
    if (-not $move) { $h = Get-ExplorerWindowFor $FolderA; if ($h) { $move = [RT]::Window([int64]$h) } }
    if (-not $move) { throw 'No folder-a window to move' }
    $newX = $move.X - 120; $newY = $move.Y + 60
    if ($newX -lt 20) { $newX = $move.X + 120 }
    [void][RT]::SetPos([int64]$move.Handle, $newX, $newY, $move.W, $move.H)
    Start-Sleep -Milliseconds 800
    $moved = [RT]::Window([int64]$move.Handle)
    Log ('Autosave moved folder-a to ' + $moved.X + ',' + $moved.Y + ' from ' + $move.X + ',' + $move.Y)
    Navigate 'Workspaces'
    # Make sure autosave is on for RT Work A (inside FlowDesk's own details UI).
    $aName = @(Get-ByName 'RT Work A' ([System.Windows.Automation.ControlType]::Text))[0]
    $aCard = if ($aName) { Find-Ancestor $aName { param($n) $all=$n.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition); @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -like 'Autosave*' }).Count -eq 1 } } else { $null }
    $autoText = ''
    if ($aCard) { $autoText = @($aCard.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -like 'Autosave*' })[0].Current.Name }
    Log ('A autosave text before move: ' + $autoText)
    if ($autoText -like 'Autosave off*') {
        $act = Get-WorkspaceActionsButton 'RT Work A'
        Invoke-Element $act; Start-Sleep -Milliseconds 600
        $edit = @(Get-ByName 'Edit' ([System.Windows.Automation.ControlType]::MenuItem) -Desktop)[0]
        if (-not $edit) { $edit = @(Get-ByName 'Edit' ([System.Windows.Automation.ControlType]::MenuItem))[0] }
        Invoke-Element $edit; Start-Sleep -Milliseconds 800
        $keep = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.Name -eq 'Keep this workspace up to date' -and (Get-ToggleState $_) -ne 'Unknown' })[0]
        if ($keep) { Set-Toggle $keep $true }
        Start-Sleep -Milliseconds 500
        Navigate 'Workspaces'; Start-Sleep -Milliseconds 600
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 20) {
        $aName = @(Get-ByName 'RT Work A' ([System.Windows.Automation.ControlType]::Text))[0]
        $aCard = if ($aName) { Find-Ancestor $aName { param($n) $all=$n.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition); @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -like 'Autosave*' }).Count -eq 1 } } else { $null }
        $autoText = ''
        if ($aCard) { $autoText = @($aCard.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -like 'Autosave*' })[0].Current.Name }
        if ($autoText -match 'saved') { break }
        Start-Sleep -Seconds 2
    }
    Log ('Autosave status text: ' + $autoText)
    # Close FlowDesk (no focus session now), reopen it. Never close an instance the test did not start.
    $app = Get-AppProcess
    if (-not $app -or -not ($script:FlowDeskOwnedPids -contains [uint32]$app.Id)) {
        throw 'BLOCKED: FlowDesk existed before the test; refusing to close a user instance for the restart step'
    }
    $allClosed = $true
    foreach ($procId in @($script:FlowDeskOwnedPids | Sort-Object -Descending)) {
        $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        $expectedStart = $script:FlowDeskOwnedStart[[uint32]$procId]
        if (-not $expectedStart -or $p.StartTime -ne $expectedStart) {
            Log ('WARN not closing pid=' + $procId + ' for restart: recorded start no longer matches')
            $allClosed = $false
            continue
        }
        Log ('Closing FlowDesk for restart: pid=' + $procId + ' hwnd=0x' + ('{0:X}' -f $p.MainWindowHandle))
        [void]$p.CloseMainWindow()
        if (-not $p.WaitForExit(20000)) { Log ('FlowDesk pid=' + $procId + ' did not exit for restart'); $allClosed = $false }
    }
    Log ('FlowDesk closed: ' + $allClosed)
    if (-not $allClosed) { throw 'FlowDesk did not close in 20 s' }
    $beforeRestart = @(Get-FlowDeskProcesses | ForEach-Object { [uint32]$_.Id })
    Start-Process -FilePath $AppExe | Out-Null
    $app2 = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 40) { $app2 = Get-AppProcess; if ($app2 -and $app2.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 500 }
    if (-not $app2) { throw 'FlowDesk did not restart' }
    Register-FlowDeskOwned @(Get-FlowDeskProcesses | Where-Object { $beforeRestart -notcontains [uint32]$_.Id } | ForEach-Object { [uint32]$_.Id })
    $fdPids = @(Get-FlowDeskProcesses | ForEach-Object { [uint32]$_.Id })
    Log ('FlowDesk restarted pid=' + $app2.Id + ' hwnd=0x' + ('{0:X}' -f $app2.MainWindowHandle) + ' ownedPids=' + ($script:FlowDeskOwnedPids -join ','))
    Start-Sleep -Seconds 3
    Register-FlowDeskOwned @(Get-FlowDeskProcesses | ForEach-Object { [uint32]$_.Id })
    $fdPids = @(Get-FlowDeskProcesses | ForEach-Object { [uint32]$_.Id })
    Navigate 'Workspaces'
    Open-Workspace 'RT Work A' | Out-Null
    $sum = Wait-Report 'RT Work A' 60
    Start-Sleep -Seconds 2
    $after = @(Get-RTWindows | Where-Object { $_.Title -like '*folder-a*' })[0]
    if (-not $after) { $h = Get-ExplorerWindowFor $FolderA; if ($h) { $after = [RT]::Window([int64]$h) } }
    if (-not $after) { throw 'folder-a window missing after reopen' }
    $delta = Rect-Delta $after $moved.X $moved.Y $moved.W $moved.H
    Log ('Autosave after reopen: rect=' + $after.X + ',' + $after.Y + ' expected=' + $moved.X + ',' + $moved.Y + ' delta=' + ($delta -join ',') + ' summary=' + $sum)
    if (@($delta | Where-Object { $_ -gt 10 }).Count -eq 0) {
        Add-Result $step 'PASS' ('moved window returns at ' + $moved.X + ',' + $moved.Y + ' after close/reopen') ('rect ' + $after.X + ',' + $after.Y + ' delta ' + ($delta -join ',') + '; autosave "' + $autoText + '"') ($sum)
    } else {
        Add-Result $step 'FAIL' ('moved window returns at ' + $moved.X + ',' + $moved.Y) ('rect ' + $after.X + ',' + $after.Y + ' delta ' + ($delta -join ',') + '; autosave "' + $autoText + '"') ($sum)
        $null = Save-Shot 'step6-autosave-fail.png'
    }
} catch {
    $msg = $_.Exception.Message
    if ($msg -like 'BLOCKED:*') {
        Add-Result $step 'BLOCKED' 'autosave keeps the moved position across restart' ($msg -replace '^BLOCKED: ','') 'no user FlowDesk instance is closed'
    } else {
        Add-Result $step 'FAIL' 'autosave keeps the moved position across restart' $msg 'see log'
    }
    $null = Save-Shot 'step6-autosave-exception.png'
}

# ---- STEP 7: cleanup ----
$step = '7 Cleanup'
try {
    Navigate 'Workspaces'
    foreach ($name in @('RT Work A','RT Work B')) {
        $found = @(Get-ByName $name ([System.Windows.Automation.ControlType]::Text)).Count -gt 0
        if (-not $found) { continue }
        $act = Get-WorkspaceActionsButton $name
        if (-not $act) { Log ('No actions button for ' + $name); continue }
        Invoke-Element $act; Start-Sleep -Milliseconds 700
        $del = @(Get-ByName 'Delete' ([System.Windows.Automation.ControlType]::MenuItem) -Desktop)[0]
        if (-not $del) { $del = @(Get-ByName 'Delete' ([System.Windows.Automation.ControlType]::MenuItem))[0] }
        if ($del) {
            Invoke-Element $del; Start-Sleep -Milliseconds 700
            $dlg = @(Get-ByName 'Delete workspace?' ([System.Windows.Automation.ControlType]::Window))[0]
            if ($dlg) {
                $confirm = @($dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq 'Delete workspace' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button })[0]
                if ($confirm) { Invoke-Element $confirm }
            }
        }
        Start-Sleep -Seconds 1
    }
    Navigate 'Workspaces'
    $texts = @(Get-UiAll (Get-AppRoot) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name })
    $leftWs = @('RT Work A','RT Work B','RT Layout' | Where-Object { $texts -contains $_ })
    $issues = Compare-UserWindows $user0 (Get-OwnedHandles) $fdPids
    Close-AllApps
    Close-FixtureWindows 'cleanup'
    Start-Sleep -Seconds 2
    # Force only the exact recorded process instance (PID + StartTime), never a recycled PID.
    $killPids = @()
    foreach ($rec in @($script:Owned | Where-Object { $_.Process -in @('notepad','mspaint','chrome') })) {
        if ($killPids -contains [uint32]$rec.AppPid) { continue }
        if (Test-RecordedProcess $rec) {
            Log ('Force-stopping recorded pid=' + $rec.AppPid + ' process=' + $rec.Process + ' start=' + $rec.Start.ToString('HH:mm:ss'))
            Stop-Process -Id $rec.AppPid -Force -ErrorAction SilentlyContinue
            $killPids += [uint32]$rec.AppPid
        } elseif (Get-Process -Id $rec.AppPid -ErrorAction SilentlyContinue) {
            Log ('WARN not killing pid=' + $rec.AppPid + ': process start no longer matches the recorded instance')
        }
    }
    Start-Sleep -Seconds 1
    $ownedHandles = @(Get-OwnedHandles)
    $remainingOwned = @([RT]::WindowsAll() | Where-Object { $ownedHandles -contains [int64]$_.Handle })
    $flowLeft = Stop-OwnedFlowDesk
    Start-Sleep -Milliseconds 700
    $remainingOwned = @([RT]::WindowsAll() | Where-Object { $ownedHandles -contains [int64]$_.Handle })
    if (Test-Path -LiteralPath $ChromeProfile) { Remove-Item -LiteralPath $ChromeProfile -Recurse -Force }
    Log ('Cleanup: remainingOwned=' + $remainingOwned.Count + ' workspacesLeft=' + ($leftWs -join ',') + ' chromeProfile=' + (Test-Path -LiteralPath $ChromeProfile) + ' testFlowDeskLeft=' + $flowLeft + ' userIssues=' + $issues.Count)
    if ($remainingOwned.Count -eq 0 -and $leftWs.Count -eq 0 -and -not (Test-Path -LiteralPath $ChromeProfile) -and $flowLeft -eq 0 -and $issues.Count -eq 0) {
        Add-Result $step 'PASS' '0 owned windows, 0 RT workspaces, chrome profile removed, test FlowDesk closed, no user window changed' ('remainingOwned=0; workspacesLeft=0; testFlowDeskLeft=0; user changes=0') (($issues -join '; '))
    } else {
        Add-Result $step 'FAIL' '0 owned windows, 0 RT workspaces, chrome profile removed, test FlowDesk closed, no user window changed' ('remainingOwned=' + $remainingOwned.Count + '; workspacesLeft=' + ($leftWs -join ',') + '; profileExists=' + (Test-Path -LiteralPath $ChromeProfile) + '; testFlowDeskLeft=' + $flowLeft + '; user changes=' + $issues.Count) (($issues -join '; '))
        $null = Save-Shot 'step7-cleanup-fail.png'
    }
} catch {
    Add-Result $step 'FAIL' 'cleanup' ($_.Exception.Message) 'see log'
}

# ---- summary ----
$pass = Get-StatusCount 'PASS'; $fail = Get-StatusCount 'FAIL'; $blocked = Get-StatusCount 'BLOCKED'
Log ''
Log ('VERDICT: PASS=' + $pass + ' FAIL=' + $fail + ' BLOCKED=' + $blocked)
foreach ($r in $script:Results) { Log ($r.Status + ' | ' + $r.Step + ' | ' + $r.Expected + ' => ' + $r.Actual) }
$verdictPath = Join-Path $RunDir 'results.json'
$script:Results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $verdictPath -Encoding UTF8
Log ('Results JSON: ' + $verdictPath)
Log ('Screenshots: ' + $ShotDir)
if ($fail -gt 0) { exit 1 }
if ($blocked -gt 0) { exit 2 }
exit 0
