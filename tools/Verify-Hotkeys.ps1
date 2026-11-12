# Verify-Hotkeys.ps1 - desktop check for FlowDesk's optional window shortcuts.
# Run only with FlowDesk running and Settings > Window shortcuts turned on.
# Opens the disposable smoke fixtures, sends Alt shortcuts with SendInput and checks the formulas.
# It refuses to run when a note.txt window or a fixture-folder Explorer window already exists, and
# closes only fixture windows it created and verified itself. It never kills Notepad and never
# touches any other window or desktop.
param(
    [int]$Tolerance = 2
)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class HotkeyVerifyNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public INPUTUNION data; }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int infoLength, out int returnLength);

    const int SW_RESTORE = 9, MONITOR_DEFAULTTONEAREST = 2, INPUT_KEYBOARD = 1, INPUT_MOUSE = 0;
    const int GWL_EXSTYLE = -20, DWMWA_CLOAKED = 14, TOKEN_ELEVATION_CLASS = 20;
    const uint KEYEVENTF_KEYUP = 0x0002, MOUSEEVENTF_WHEEL = 0x0800, WM_CLOSE = 0x0010;
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x0008;
    const long WS_EX_TOOLWINDOW = 0x00000080L;
    static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

    public static bool EnableDpiAwareness() { return SetProcessDpiAwarenessContext(new IntPtr(-4)); }
    public static IntPtr Foreground() { return GetForegroundWindow(); }
    public static bool Zoomed(IntPtr hWnd) { return IsZoomed(hWnd); }
    public static uint ProcessId(IntPtr hWnd) { uint pid; GetWindowThreadProcessId(hWnd, out pid); return pid; }
    public static IntPtr[] TopLevelWindows()
    {
        List<IntPtr> handles = new List<IntPtr>();
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) { handles.Add(hWnd); return true; }, IntPtr.Zero);
        return handles.ToArray();
    }
    public static string Title(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0) return "";
        StringBuilder text = new StringBuilder(length + 1);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }
    public static int[] Rect(IntPtr hWnd)
    {
        RECT r;
        if (!GetWindowRect(hWnd, out r)) return new int[] { 0, 0, 0, 0 };
        return new int[] { r.Left, r.Top, r.Right, r.Bottom };
    }
    public static int[] WorkArea(IntPtr hWnd)
    {
        RECT r;
        if (!GetWindowRect(hWnd, out r)) return new int[] { 0, 0, 0, 0 };
        POINT center = new POINT();
        center.X = (r.Left + r.Right) / 2;
        center.Y = (r.Top + r.Bottom) / 2;
        IntPtr monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        MONITORINFO info = new MONITORINFO();
        info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return new int[] { 0, 0, 0, 0 };
        return new int[] { info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom };
    }
    public static bool Activate(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        ShowWindowAsync(hWnd, SW_RESTORE);
        if (SetForegroundWindow(hWnd)) return true;
        IntPtr foreground = GetForegroundWindow();
        uint unused;
        uint foregroundThread = GetWindowThreadProcessId(foreground, out unused);
        uint ownThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != ownThread
            && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hWnd);
            return SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached) AttachThreadInput(ownThread, foregroundThread, false);
        }
    }
    public static void KeyDown(ushort vk) { Send(vk, false); }
    public static void KeyUp(ushort vk) { Send(vk, true); }
    static void Send(ushort vk, bool up)
    {
        INPUT input = new INPUT();
        input.type = INPUT_KEYBOARD;
        input.data.ki.wVk = vk;
        input.data.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        SendInput(1, new INPUT[] { input }, InputSize);
    }
    public static void Wheel(int delta)
    {
        INPUT input = new INPUT();
        input.type = INPUT_MOUSE;
        input.data.mi.mouseData = unchecked((uint)delta);
        input.data.mi.dwFlags = MOUSEEVENTF_WHEEL;
        SendInput(1, new INPUT[] { input }, InputSize);
    }
    public static void ReleaseModifiers()
    {
        ushort[] keys = new ushort[] { 0x12, 0x10, 0x11, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };
        foreach (ushort key in keys) KeyUp(key);
    }
    public static bool Visible(IntPtr hWnd) { return IsWindowVisible(hWnd); }
    public static bool Minimized(IntPtr hWnd) { return IsIconic(hWnd); }
    public static bool Cloaked(IntPtr hWnd)
    {
        int value;
        return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out value, sizeof(int)) == 0 && value != 0;
    }
    public static string ClassName(IntPtr hWnd)
    {
        StringBuilder text = new StringBuilder(256);
        return GetClassName(hWnd, text, text.Capacity) > 0 ? text.ToString() : "";
    }
    public static bool ToolWindow(IntPtr hWnd)
    {
        return (GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;
    }
    public static bool MoveToRect(IntPtr hWnd, int[] rect)
    {
        if (hWnd == IntPtr.Zero || rect == null || rect.Length < 4) return false;
        return SetWindowPos(hWnd, IntPtr.Zero, rect[0], rect[1], rect[2] - rect[0], rect[3] - rect[1], SWP_NOZORDER | SWP_NOACTIVATE);
    }
    // 1 = elevated, 0 = not elevated, -1 = elevation could not be read and is treated as unsafe.
    public static int ElevationState(IntPtr hWnd)
    {
        uint pid = ProcessId(hWnd);
        if (pid == 0) return -1;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return -1;
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out token) || token == IntPtr.Zero) return -1;
            int elevated;
            int returned;
            if (!GetTokenInformation(token, TOKEN_ELEVATION_CLASS, out elevated, sizeof(int), out returned)) return -1;
            return elevated != 0 ? 1 : 0;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            CloseHandle(process);
        }
    }
    public static void Close(IntPtr hWnd) { PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
}
'@

$fixture = Join-Path $PSScriptRoot 'smoke-fixture'
$notePath = Join-Path $fixture 'note.txt'
$fixtureLeaf = Split-Path -Leaf $fixture
if (-not (Test-Path -LiteralPath $notePath)) { throw "Fixture file not found: $notePath" }
if (-not (Get-Process FlowDesk.App -ErrorAction SilentlyContinue)) {
    throw 'Start FlowDesk and turn on Settings > Window shortcuts before running this check.'
}

[void][HotkeyVerifyNative]::EnableDpiAwareness()
$beforeHandles = @([HotkeyVerifyNative]::TopLevelWindows())
$existingNote = @($beforeHandles | Where-Object { [HotkeyVerifyNative]::Title($_) -like '*note.txt*' })
if ($existingNote.Count -gt 0) {
    throw 'A window with note.txt is already open. Close the disposable fixtures first; nothing was touched.'
}
$shell = New-Object -ComObject Shell.Application
$existingFolders = @($shell.Windows() | Where-Object {
    try { $_.Document.Folder.Self.Path -eq $fixture } catch { $false }
})
if ($existingFolders.Count -gt 0) {
    throw 'An Explorer window for the fixture folder is already open. Close it first; nothing was touched.'
}
$existingTitles = @($beforeHandles | Where-Object { [HotkeyVerifyNative]::Title($_) -eq $fixtureLeaf })
if ($existingTitles.Count -gt 0) {
    throw 'A window titled like the fixture folder already exists. Nothing was touched.'
}

$failures = 0
$skips = 0
$shellClasses = @('Progman', 'WorkerW', 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd')
function Pass([string]$Step, [string]$Detail) { Write-Output "PASS $Step $Detail" }
function Fail([string]$Step, [string]$Detail) { Write-Output "FAIL $Step $Detail"; $script:failures++ }

function Require-Fixture([IntPtr]$Handle, [string]$Step)
{
    for ($i = 0; $i -lt 30; $i++)
    {
        if ([HotkeyVerifyNative]::Foreground() -eq $Handle) { return }
        Start-Sleep -Milliseconds 50
    }
    Fail $Step 'foreground is not the fixture'
    throw "The expected fixture window is not in the foreground before $Step."
}

function Focus-Fixture([IntPtr]$Handle, [string]$Step)
{
    if (-not [HotkeyVerifyNative]::Activate($Handle)) { throw "Could not foreground the fixture before $Step." }
    Start-Sleep -Milliseconds 300
    Require-Fixture $Handle $Step
}

function Invoke-AltCombo([int]$VirtualKey, [switch]$Shift)
{
    try
    {
        [HotkeyVerifyNative]::KeyDown(0x12)
        if ($Shift) { [HotkeyVerifyNative]::KeyDown(0x10) }
        Start-Sleep -Milliseconds 40
        [HotkeyVerifyNative]::KeyDown([uint16]$VirtualKey)
        Start-Sleep -Milliseconds 40
        [HotkeyVerifyNative]::KeyUp([uint16]$VirtualKey)
        Start-Sleep -Milliseconds 40
        if ($Shift) { [HotkeyVerifyNative]::KeyUp(0x10) }
        [HotkeyVerifyNative]::KeyUp(0x12)
    }
    finally
    {
        [HotkeyVerifyNative]::KeyUp([uint16]$VirtualKey)
        if ($Shift) { [HotkeyVerifyNative]::KeyUp(0x10) }
        [HotkeyVerifyNative]::KeyUp(0x12)
    }
    Start-Sleep -Milliseconds 500
}

function Invoke-AltShiftWheel([int]$Delta)
{
    try
    {
        [HotkeyVerifyNative]::KeyDown(0x12)
        [HotkeyVerifyNative]::KeyDown(0x10)
        Start-Sleep -Milliseconds 120
        [HotkeyVerifyNative]::Wheel($Delta)
    }
    finally
    {
        [HotkeyVerifyNative]::ReleaseModifiers()
    }
    Start-Sleep -Milliseconds 500
}

function Invoke-AltShiftDoubleTap([int]$VirtualKey)
{
    try
    {
        [HotkeyVerifyNative]::KeyDown(0x12)
        [HotkeyVerifyNative]::KeyDown(0x10)
        Start-Sleep -Milliseconds 40
        [HotkeyVerifyNative]::KeyDown([uint16]$VirtualKey)
        Start-Sleep -Milliseconds 40
        [HotkeyVerifyNative]::KeyUp([uint16]$VirtualKey)
        Start-Sleep -Milliseconds 40
        [HotkeyVerifyNative]::KeyDown([uint16]$VirtualKey)
        Start-Sleep -Milliseconds 40
        [HotkeyVerifyNative]::KeyUp([uint16]$VirtualKey)
        Start-Sleep -Milliseconds 40
        [HotkeyVerifyNative]::KeyUp(0x10)
        [HotkeyVerifyNative]::KeyUp(0x12)
    }
    finally
    {
        [HotkeyVerifyNative]::KeyUp([uint16]$VirtualKey)
        [HotkeyVerifyNative]::KeyUp(0x10)
        [HotkeyVerifyNative]::KeyUp(0x12)
    }
    Start-Sleep -Milliseconds 500
}

function Assert-Rect([string]$Step, [IntPtr]$Handle, [int[]]$Expected)
{
    $actual = [HotkeyVerifyNative]::Rect($Handle)
    $ok = $true
    for ($i = 0; $i -lt 4; $i++)
    {
        if ([Math]::Abs($actual[$i] - $Expected[$i]) -gt $Tolerance) { $ok = $false }
    }
    if ($ok) { Pass $Step ("expected={0} actual={1}" -f ($Expected -join ','), ($actual -join ',')) }
    else { Fail $Step ("expected={0} actual={1}" -f ($Expected -join ','), ($actual -join ',')) }
}

function Snap-Rect([int[]]$Area, [string]$Direction)
{
    $left = $Area[0]; $top = $Area[1]; $right = $Area[2]; $bottom = $Area[3]
    $width = $right - $left; $height = $bottom - $top
    switch ($Direction)
    {
        'left'
        {
            $half = [int][Math]::Floor(($width + 8) / 2.0)
            return @($left, $top, ($left + $half), $bottom)
        }
        'right'
        {
            $half = [int][Math]::Floor(($width + 8) / 2.0)
            $x = $left + $half - 8
            return @($x, $top, $right, $bottom)
        }
        'up'
        {
            $half = [int][Math]::Floor(($height + 8) / 2.0)
            return @($left, $top, $right, ($top + $half))
        }
        'down'
        {
            $half = [int][Math]::Floor(($height + 8) / 2.0)
            $y = $top + $half - 8
            return @($left, $y, $right, $bottom)
        }
    }
}

function Center-Rect([int[]]$Area, [int[]]$WindowRect)
{
    $width = $WindowRect[2] - $WindowRect[0]; $height = $WindowRect[3] - $WindowRect[1]
    $x = [int][Math]::Round($Area[0] + (($Area[2] - $Area[0]) - $width) / 2.0, [MidpointRounding]::AwayFromZero)
    $y = [int][Math]::Round($Area[1] + (($Area[3] - $Area[1]) - $height) / 2.0, [MidpointRounding]::AwayFromZero)
    return @($x, $y, ($x + $width), ($y + $height))
}

function Resize-Rect([int[]]$Area, [int[]]$WindowRect, [bool]$Grow)
{
    $left = $Area[0]; $top = $Area[1]; $right = $Area[2]; $bottom = $Area[3]
    $areaWidth = $right - $left; $areaHeight = $bottom - $top
    $width = $WindowRect[2] - $WindowRect[0]; $height = $WindowRect[3] - $WindowRect[1]
    $cx = $WindowRect[0] + $width / 2.0; $cy = $WindowRect[1] + $height / 2.0
    $factor = if ($Grow) { 1.08 } else { 0.92 }
    $newWidth = [int][Math]::Round($width * $factor, [MidpointRounding]::AwayFromZero)
    $newHeight = [int][Math]::Round($height * $factor, [MidpointRounding]::AwayFromZero)
    $newWidth = [Math]::Max($newWidth, 300); $newHeight = [Math]::Max($newHeight, 200)
    $newWidth = [Math]::Min($newWidth, $areaWidth); $newHeight = [Math]::Min($newHeight, $areaHeight)
    $x = [int][Math]::Round($cx - $newWidth / 2.0, [MidpointRounding]::AwayFromZero)
    $y = [int][Math]::Round($cy - $newHeight / 2.0, [MidpointRounding]::AwayFromZero)
    if ($x -lt $left) { $x = $left }
    if ($y -lt $top) { $y = $top }
    if (($x + $newWidth) -gt $right) { $x = $right - $newWidth }
    if (($y + $newHeight) -gt $bottom) { $y = $bottom - $newHeight }
    return @($x, $y, ($x + $newWidth), ($y + $newHeight))
}

# Every top-level window in EnumWindows order, filtered exactly like DirectionalFocus.Usable.
function Get-UsableWindows([IntPtr]$Active)
{
    $activeValue = $Active.ToInt64()
    $list = New-Object System.Collections.Generic.List[object]
    $index = 0
    foreach ($raw in [HotkeyVerifyNative]::TopLevelWindows())
    {
        $handle = [IntPtr]$raw
        $z = $index
        $index++
        if ($handle.ToInt64() -eq $activeValue) { continue }
        if (-not [HotkeyVerifyNative]::Visible($handle)) { continue }
        if ([HotkeyVerifyNative]::Cloaked($handle)) { continue }
        if ([HotkeyVerifyNative]::Minimized($handle)) { continue }
        if ([string]::IsNullOrEmpty([HotkeyVerifyNative]::Title($handle))) { continue }
        if ($shellClasses -ccontains [HotkeyVerifyNative]::ClassName($handle)) { continue }
        if ([HotkeyVerifyNative]::ToolWindow($handle)) { continue }
        $list.Add([pscustomobject]@{ Handle = $handle; Rect = [HotkeyVerifyNative]::Rect($handle); Index = $z })
    }
    return $list
}

# Left candidates scored as |dx| + 1.5 * |dy| using outer-rect centers. The explicit z-index
# tiebreak is the same order the stable OrderBy in DirectionalFocus.Rank preserves.
function Get-LeftRanked([IntPtr]$Active)
{
    $activeRect = [HotkeyVerifyNative]::Rect($Active)
    $ax = ($activeRect[0] + $activeRect[2]) / 2.0
    $ay = ($activeRect[1] + $activeRect[3]) / 2.0
    $ranked = New-Object System.Collections.Generic.List[object]
    foreach ($window in @(Get-UsableWindows $Active))
    {
        $rect = $window.Rect
        $dx = (($rect[0] + $rect[2]) / 2.0) - $ax
        if (-not ($dx -lt 0)) { continue }
        $dy = (($rect[1] + $rect[3]) / 2.0) - $ay
        $score = [Math]::Abs($dx) + 1.5 * [Math]::Abs($dy)
        $ranked.Add([pscustomobject]@{ Handle = $window.Handle; Score = $score; Index = $window.Index })
    }
    return @($ranked | Sort-Object -Property Score, Index)
}

$notepad = $null
$notepadStarted = $false
$explorerStarted = $false
$newNotepadHandle = [IntPtr]::Zero
$newExplorerHandle = [IntPtr]::Zero
$verifiedNotepad = $false
$verifiedExplorer = $false

try
{
    $notepad = Start-Process -FilePath notepad.exe -ArgumentList "`"$notePath`"" -PassThru
    $notepadStarted = $true
    for ($i = 0; $i -lt 40; $i++)
    {
        Start-Sleep -Milliseconds 200
        $newHandles = @([HotkeyVerifyNative]::TopLevelWindows()) | Where-Object { $beforeHandles -notcontains $_ }
        $candidates = @($newHandles | Where-Object { [HotkeyVerifyNative]::Title($_) -like '*note.txt*' })
        if ($candidates.Count -eq 1) { $newNotepadHandle = [IntPtr]$candidates[0]; break }
        if ($candidates.Count -gt 1) { throw 'Ambiguous new note.txt windows appeared; leaving them open.' }
    }
    if ($newNotepadHandle -eq [IntPtr]::Zero) { throw 'The new Notepad fixture window could not be verified; leaving it open.' }
    $verifiedNotepad = $true

    Start-Process -FilePath explorer.exe -ArgumentList "`"$fixture`"" | Out-Null
    $explorerStarted = $true
    $folders = @()
    for ($i = 0; $i -lt 40; $i++)
    {
        Start-Sleep -Milliseconds 200
        $folders = @($shell.Windows() | Where-Object {
            try { $_.Document.Folder.Self.Path -eq $fixture } catch { $false }
        })
        if ($folders.Count -eq 1) { break }
        if ($folders.Count -gt 1) { throw 'Ambiguous Explorer fixture windows appeared; leaving them open.' }
    }
    if ($folders.Count -ne 1) { throw 'The new Explorer fixture window could not be verified; leaving it open.' }
    $candidateExplorer = [IntPtr][int64]$folders[0].HWND
    if ($candidateExplorer -eq [IntPtr]::Zero) { throw 'The Explorer fixture handle is empty; leaving it open.' }
    if ($beforeHandles -contains $candidateExplorer) { throw 'Explorer reused an existing window; leaving it open.' }
    $newExplorerHandle = $candidateExplorer
    $verifiedExplorer = $true

    # 1. Snap steps (Alt+A/D/W/S) on the verified Notepad fixture.
    Focus-Fixture $newNotepadHandle 'snap left'
    $work = [HotkeyVerifyNative]::WorkArea($newNotepadHandle)
    Invoke-AltCombo 0x41
    Assert-Rect 'snap left' $newNotepadHandle (Snap-Rect $work 'left')

    Require-Fixture $newNotepadHandle 'snap right'
    Invoke-AltCombo 0x44
    Assert-Rect 'snap right' $newNotepadHandle (Snap-Rect $work 'right')

    Require-Fixture $newNotepadHandle 'snap up'
    Invoke-AltCombo 0x57
    Assert-Rect 'snap up' $newNotepadHandle (Snap-Rect $work 'up')

    Require-Fixture $newNotepadHandle 'snap down'
    Invoke-AltCombo 0x53
    Assert-Rect 'snap down' $newNotepadHandle (Snap-Rect $work 'down')

    # 2. Center (Alt+Shift+Space), size unchanged.
    Require-Fixture $newNotepadHandle 'center'
    $before = [HotkeyVerifyNative]::Rect($newNotepadHandle)
    Invoke-AltCombo 0x20 -Shift
    Assert-Rect 'center' $newNotepadHandle (Center-Rect $work $before)

    # 3. Maximize then restore with Alt+F twice.
    Require-Fixture $newNotepadHandle 'maximize'
    Invoke-AltCombo 0x46
    if ([HotkeyVerifyNative]::Zoomed($newNotepadHandle)) { Pass 'maximize' 'IsZoomed=True' } else { Fail 'maximize' 'IsZoomed=False' }
    Require-Fixture $newNotepadHandle 'restore'
    Invoke-AltCombo 0x46
    if (-not [HotkeyVerifyNative]::Zoomed($newNotepadHandle)) { Pass 'restore' 'IsZoomed=False' } else { Fail 'restore' 'IsZoomed=True' }

    # 4. Wheel resize: two grow events, then two shrink events, checked one by one.
    Require-Fixture $newNotepadHandle 'wheel grow 1'
    $before = [HotkeyVerifyNative]::Rect($newNotepadHandle)
    Invoke-AltShiftWheel 120
    Assert-Rect 'wheel grow 1' $newNotepadHandle (Resize-Rect $work $before $true)

    Require-Fixture $newNotepadHandle 'wheel grow 2'
    $before = [HotkeyVerifyNative]::Rect($newNotepadHandle)
    Invoke-AltShiftWheel 120
    Assert-Rect 'wheel grow 2' $newNotepadHandle (Resize-Rect $work $before $true)

    Require-Fixture $newNotepadHandle 'wheel shrink 1'
    $before = [HotkeyVerifyNative]::Rect($newNotepadHandle)
    Invoke-AltShiftWheel -120
    Assert-Rect 'wheel shrink 1' $newNotepadHandle (Resize-Rect $work $before $false)

    Require-Fixture $newNotepadHandle 'wheel shrink 2'
    $before = [HotkeyVerifyNative]::Rect($newNotepadHandle)
    Invoke-AltShiftWheel -120
    Assert-Rect 'wheel shrink 2' $newNotepadHandle (Resize-Rect $work $before $false)

    # 5. Directional focus: move the Explorer fixture onto the Notepad monitor (SetWindowPos into
    # that work area), snap Notepad left and Explorer right, then verify the foreground against the
    # same filter/ranking rules as DirectionalFocus.cs. Expected targets are recomputed after a
    # fresh fixture foreground before each injected step and checked for elevation first.
    $work = [HotkeyVerifyNative]::WorkArea($newNotepadHandle)
    # Explorer can inherit a maximized launch state. Restore it BEFORE moving, otherwise the
    # following activation could restore its saved rectangle back onto the original monitor.
    Focus-Fixture $newExplorerHandle 'focus monitor setup'
    if (-not [HotkeyVerifyNative]::MoveToRect($newExplorerHandle, $work)) {
        throw 'Could not move the Explorer fixture onto the Notepad monitor.'
    }
    Start-Sleep -Milliseconds 300
    Focus-Fixture $newNotepadHandle 'focus setup left'
    Invoke-AltCombo 0x41
    Assert-Rect 'focus setup left' $newNotepadHandle (Snap-Rect $work 'left')
    Focus-Fixture $newExplorerHandle 'focus setup right'
    Invoke-AltCombo 0x44
    Assert-Rect 'focus setup right' $newExplorerHandle (Snap-Rect $work 'right')

    $focusUnsafe = $false
    # Single Alt+Shift+A: the expected target is rank 1 to the left of the foreground fixture.
    Focus-Fixture $newExplorerHandle 'focus left'
    $singleRanked = @(Get-LeftRanked $newExplorerHandle)
    if ($singleRanked.Count -eq 0) {
        Fail 'focus left' 'no directional candidate'
    } else {
        $expectedSingle = $singleRanked[0].Handle
        $singleState = [HotkeyVerifyNative]::ElevationState($expectedSingle)
        if ($singleState -ne 0) {
            $reason = if ($singleState -eq 1) { 'target process is elevated' } else { 'target process elevation could not be read' }
            Write-Output ("SKIP focus left expected={0} reason={1}" -f $expectedSingle, $reason)
            $skips++
            $focusUnsafe = $true
        } else {
            Invoke-AltCombo 0x41 -Shift
            Start-Sleep -Milliseconds 500
            $foreground = [HotkeyVerifyNative]::Foreground()
            if ($foreground -eq $expectedSingle) { Pass 'focus left' "foreground=$foreground expected=$expectedSingle" }
            else { Fail 'focus left' "expected=$expectedSingle actual=$foreground" }
        }
    }

    # Double press: two Alt+Shift+A taps 40 ms apart inside one modifier hold. FlowDesk's 140 ms
    # multi-press window turns them into one action with steps=2, which Pick clamps to the candidate
    # count, so the expected target is rank min(2, count) from the same frozen fixture state.
    if ($focusUnsafe) {
        Write-Output 'SKIP focus double reason=an earlier focus target was unsafe'
        $skips++
    } else {
        Focus-Fixture $newExplorerHandle 'focus double'
        $doubleRanked = @(Get-LeftRanked $newExplorerHandle)
        if ($doubleRanked.Count -eq 0) {
            Fail 'focus double' 'no directional candidate'
        } else {
            $steps = [Math]::Min(2, $doubleRanked.Count)
            $expectedDouble = $doubleRanked[$steps - 1].Handle
            $doubleState = [HotkeyVerifyNative]::ElevationState($expectedDouble)
            if ($doubleState -ne 0) {
                $reason = if ($doubleState -eq 1) { 'target process is elevated' } else { 'target process elevation could not be read' }
                Write-Output ("SKIP focus double expected={0} reason={1}" -f $expectedDouble, $reason)
                $skips++
            } else {
                Invoke-AltShiftDoubleTap 0x41
                Start-Sleep -Milliseconds 500
                $foreground = [HotkeyVerifyNative]::Foreground()
                if ($foreground -eq $expectedDouble) { Pass 'focus double' "foreground=$foreground expected=$expectedDouble steps=$steps" }
                else { Fail 'focus double' "expected=$expectedDouble actual=$foreground steps=$steps" }
            }
        }
    }
}
finally
{
    [HotkeyVerifyNative]::ReleaseModifiers()
    # Only windows captured and verified as NEW fixtures are closed. No Kill and no Quit loop:
    # a window that cannot be proven to be ours is left open and reported.
    try { if ($verifiedNotepad -and $newNotepadHandle -ne [IntPtr]::Zero) { [HotkeyVerifyNative]::Close($newNotepadHandle) } } catch { }
    try { if ($verifiedExplorer -and $newExplorerHandle -ne [IntPtr]::Zero) { [HotkeyVerifyNative]::Close($newExplorerHandle) } } catch { }
    Start-Sleep -Milliseconds 500
    if (($notepadStarted -and -not $verifiedNotepad) -or ($explorerStarted -and -not $verifiedExplorer)) {
        Write-Output 'NOTE: an unverified fixture window was left open on purpose.'
    }
    try { if ($null -ne $notepad) { $notepad.Dispose() } } catch { }
}

if ($failures -gt 0) { Write-Output "FAILED: $failures step(s) did not match."; exit 1 }
if ($skips -gt 0) { Write-Output "PASS: all evaluated shortcut steps matched ($skips unsafe step(s) skipped)." }
else { Write-Output 'PASS: all shortcut steps matched.' }
