using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Diagnostics;
using FlowDesk.Core.Desktops;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.QuickLook;

namespace FlowDesk.App.Services;

/// <summary>
/// Owns global window shortcuts for as long as the feature is on. One dedicated thread runs the
/// message-only window, the RegisterHotKey ids and the low-level mouse hook; that thread only checks
/// keys/cooldown, submits the small menu-mask/release batch and queues actions. One serialized worker thread runs every action and the timed focus step,
/// so actions never overlap. Stop unregisters, unhooks, joins and drains stale work.
/// </summary>
public sealed class HotkeyHost : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_QUIT = 0x0012;
    private const int WM_WTSSESSION_CHANGE = 0x02B1;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WH_MOUSE_LL = 14;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    private const uint ModNoRepeat = 0x4000;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const ushort VkUnassigned = 0xE8;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private static readonly nint HWND_MESSAGE = -3;
    private static readonly string WindowClassName = "FlowDesk.HotkeyWindow";
    private static readonly WndProcDelegate WindowProc = WndProc;
    private static HotkeyHost? _current;
    private static bool _classRegistered;

    private readonly IVirtualDesktops? _desktops;
    private readonly Action<string>? _notify;
    private readonly WindowShortcutActions _actions;
    private readonly MultiPressQueue _multiPress = new(new TimerOneShotScheduler());
    private readonly BlockingCollection<Action> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _conflicts = [];
    private readonly Dictionary<int, HotkeyBinding> _registered = [];
    private readonly HookProc _hookProc;
    private readonly HookProc _keyboardProc;
    private readonly WheelGestures _wheel = new();
    private readonly ShortcutKeyboardState _keyboard = new();
    private readonly QuickLookTrigger _quickLookTrigger = new();
    private IQuickLookInput? _quickLook;
    private readonly bool _windowShortcuts;
#if DEBUG
    private readonly string? _inputDiagnosticLog = Environment.GetEnvironmentVariable("FLOWDESK_HOTKEY_DIAGNOSTICS_LOG");
    private readonly string _inputDiagnosticHost = Guid.NewGuid().ToString("N")[..8];
    private static readonly object InputDiagnosticFileGate = new();
    private long _inputDiagnosticSequence;
#endif
    private readonly Action? _openTerminal;
    private readonly Action? _reload;
    private Thread? _messageThread;
    private Thread? _workerThread;
    private uint _threadId;
    private nint _hwnd;
    private nint _hook;
    private nint _keyboardHook;
    private bool _sessionNotificationsRegistered;
    private int _stopping;

    public HotkeyHost(IWindowOps windows, IVirtualDesktops? desktops, Action<string>? notify,
        Action? openTerminal = null, Action? reload = null, bool windowShortcuts = true, IQuickLookInput? quickLook = null)
    {
        _desktops = desktops;
        _notify = notify;
        _actions = new WindowShortcutActions(windows, desktops, new CancellableDelay());
        _hookProc = MouseHookProc;
        _keyboardProc = KeyboardHookProc;
        _openTerminal = openTerminal;
        _reload = reload;
        _windowShortcuts = windowShortcuts;
        _quickLook = quickLook;
    }

    public bool IsRunning { get; private set; }

    /// <summary>Shortcuts Windows refused because another app already owns them.</summary>
    public IReadOnlyList<string> Conflicts
    {
        get { lock (_conflicts) return _conflicts.ToArray(); }
    }

    public bool WheelHookAvailable { get; private set; }
    public bool WinWheelHookAvailable { get; private set; }
    public bool KeyboardHookAvailable => _keyboardHook != 0;
    public void SetQuickLook(IQuickLookInput? input) => Volatile.Write(ref _quickLook, input);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_messageThread is not null) throw new InvalidOperationException("The hotkey host is already started.");
        _multiPress.Fired += OnMultiPressFired;
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "FlowDesk hotkey actions" };
        _workerThread.Start();
        _messageThread = new Thread(MessageLoop) { IsBackground = true, Name = "FlowDesk hotkeys" };
        _messageThread.Start();

        bool ready;
        try { ready = await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { ready = false; }
        catch (OperationCanceledException) { await StopAsync().ConfigureAwait(false); throw; }

        if (!ready)
        {
            await StopAsync().ConfigureAwait(false);
            throw new InvalidOperationException("The window shortcut message window could not be created.");
        }
        IsRunning = true;
    }

    /// <summary>Unregisters, unhooks and joins. No queued action runs after this returns.</summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _multiPress.Fired -= OnMultiPressFired;
            _multiPress.Reset();
            _cts.Cancel();
            if (_threadId != 0) HotkeyNativeMethods.PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
            try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { }
        }
        await JoinThreadsAsync().ConfigureAwait(false);
        _actions.SetPaused(false);
        IsRunning = false;
        WheelHookAvailable = false;
        WinWheelHookAvailable = false;
    }

    private Task JoinThreadsAsync()
    {
        var messageThread = _messageThread;
        var workerThread = _workerThread;
        return Task.Run(() =>
        {
            // Do not report a stopped host (or dispose its queue/delegates) while either owner
            // is alive. Native desktop calls are bounded by the backend; short waits cancel.
            messageThread?.Join();
            workerThread?.Join();
        });
    }

    private void MessageLoop()
    {
        _current = this;
        try
        {
            _threadId = HotkeyNativeMethods.GetCurrentThreadId();
            EnsureWindowClass();
            _hwnd = HotkeyNativeMethods.CreateWindowExW(0, WindowClassName, "", 0, 0, 0, 0, 0,
                HWND_MESSAGE, 0, HotkeyNativeMethods.GetModuleHandleW(null), 0);
            if (_hwnd == 0)
            {
                _ready.TrySetResult(false);
                return;
            }

            // Stop can race thread startup, before Windows has created the thread queue.
            // In that case PostThreadMessage may fail, so cancellation is also checked here.
            if (Volatile.Read(ref _stopping) != 0) return;

            if (_windowShortcuts) RegisterBindings();
            _sessionNotificationsRegistered = HotkeyNativeMethods.WTSRegisterSessionNotification(_hwnd, 0); // this session
            TraceWinInput($"session-notifications registered={_sessionNotificationsRegistered} error={(_sessionNotificationsRegistered ? 0 : Marshal.GetLastWin32Error())}");
            _keyboard.Seed(IsDown);
            if (_windowShortcuts) _hook = HotkeyNativeMethods.SetWindowsHookExW(WH_MOUSE_LL, _hookProc, HotkeyNativeMethods.GetModuleHandleW(null), 0);
            _keyboardHook = HotkeyNativeMethods.SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, HotkeyNativeMethods.GetModuleHandleW(null), 0);
            if (_quickLook is not null && _keyboardHook == 0) throw new InvalidOperationException("Quick Look keyboard hook could not be installed.");
            WheelHookAvailable = _hook != 0;
            WinWheelHookAvailable = _hook != 0 && _keyboardHook != 0;
            _ready.TrySetResult(true);

            while (Volatile.Read(ref _stopping) == 0 &&
                   HotkeyNativeMethods.GetMessageW(out MSG message, 0, 0, 0) > 0)
            {
                HotkeyNativeMethods.TranslateMessage(ref message);
                HotkeyNativeMethods.DispatchMessageW(ref message);
            }
        }
        catch (Exception)
        {
            _ready.TrySetResult(false);
        }
        finally
        {
            Interlocked.Exchange(ref _stopping, 1);
            _cts.Cancel();
            _multiPress.Reset();
            _queue.CompleteAdding();
            if (_keyboardHook != 0)
            {
                HotkeyNativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = 0;
            }
            if (_hook != 0)
            {
                HotkeyNativeMethods.UnhookWindowsHookEx(_hook);
                _hook = 0;
            }
            foreach (int id in _registered.Keys) HotkeyNativeMethods.UnregisterHotKey(_hwnd, id);
            _registered.Clear();
            if (_sessionNotificationsRegistered)
            {
                HotkeyNativeMethods.WTSUnRegisterSessionNotification(_hwnd);
                _sessionNotificationsRegistered = false;
            }
            if (_hwnd != 0)
            {
                HotkeyNativeMethods.DestroyWindow(_hwnd);
                _hwnd = 0;
            }
            if (ReferenceEquals(_current, this)) _current = null;
            IsRunning = false;
            WheelHookAvailable = false;
            WinWheelHookAvailable = false;
        }
    }

    private void RegisterBindings()
    {
        bool desktopsAvailable = _desktops?.IsAvailable == true;
        int id = 1;
        foreach (var binding in HotkeyCatalog.Registerable)
        {
            if (binding.IsDesktop && !desktopsAvailable)
            {
                id++;
                continue;
            }
            uint modifiers = binding.Modifiers | (binding.Repeat ? 0u : ModNoRepeat);
            if (HotkeyNativeMethods.RegisterHotKey(_hwnd, id, modifiers, binding.VirtualKey))
            {
                _registered[id] = binding;
            }
            else
            {
                lock (_conflicts) _conflicts.Add(binding.Shortcut);
            }
            id++;
        }
    }

    private void OnHotkey(int id)
    {
        if (!_registered.TryGetValue(id, out var binding)) return;
        SendAltMenuMask();
        Enqueue(() => HandleBinding(binding));
    }

    internal bool IsPaused => _actions.IsPaused;

    internal void HandleBinding(HotkeyBinding binding)
    {
        if (binding.Id == "terminal-open") { _openTerminal?.Invoke(); return; }
        if (binding.Id == "engine-reload") { _reload?.Invoke(); return; }
        if (binding.Id == HotkeyCatalog.PauseToggleId || binding.Id == "pause")
        {
            bool paused = binding.Id == "pause" || !_actions.IsPaused;
            _actions.SetPaused(paused);
            _multiPress.Reset();
            _notify?.Invoke(paused ? "Window shortcuts paused" : "Window shortcuts on");
            return;
        }
        if (HotkeyCatalog.TryGetFocusDirection(binding, out var direction))
        {
            if (_actions.IsPaused) return;
            _multiPress.Press(direction);
            return;
        }
        _actions.Execute(binding, _cts.Token);
    }

    private void OnMultiPressFired(WindowDirection direction, int steps) =>
        Enqueue(() =>
        {
            if (!_actions.IsPaused) _actions.Focus(direction, steps, _cts.Token);
        });

    private void Enqueue(Action action)
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        try { _queue.Add(action); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
    }

    private void WorkerLoop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            if (Volatile.Read(ref _stopping) != 0) continue;
            try { action(); } catch (Exception) { /* an action failure must not stop the worker */ }
        }
    }

    private nint MouseHookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && wParam == WM_MOUSEWHEEL && Volatile.Read(ref _stopping) == 0)
        {
            try
            {
                if (_keyboardHook != 0 && _keyboard.ReconcileStale(IsDown))
                {
                    _wheel.ObserveWin(_keyboard.Modifiers.Win);
                    _wheel.ObserveAlt(_keyboard.Modifiers.Alt);
                    Enqueue(() => _actions.Search.Reset());
                    TraceWinInput($"resync=stale graceMs={ShortcutKeyboardState.StaleDownGraceMilliseconds} modifiers={_keyboard.Modifiers}");
                }
                var modifiers = _keyboardHook != 0 ? _keyboard.Modifiers : new ShortcutModifiers(
                    IsDown(VK_LWIN) || IsDown(VK_RWIN), IsDown(VK_MENU), IsDown(VK_SHIFT), IsDown(VK_CONTROL));
                // Without a keyboard hook we cannot safely mask Win release; resize still works.
                if (modifiers.Win && _keyboardHook == 0) return HotkeyNativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (info.ExtraInfo == ShortcutKeyboardState.InjectionTag)
                    return HotkeyNativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                int delta = (short)((info.MouseData >> 16) & 0xFFFF);
                if (delta == 0) return HotkeyNativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                var decision = _wheel.Accept(modifiers.Win, modifiers.Alt, modifiers.Shift, modifiers.Control, Environment.TickCount64);
                if (decision.Gesture != WheelGesture.None)
                {
                    if (decision.Accepted)
                    {
                        if (modifiers.Win)
                        {
                            _keyboard.AcceptWinGesture();
                            TraceWinInput($"gesture={decision.Gesture} accepted=true leftHold={_keyboard.LeftHoldId} rightHold={_keyboard.RightHoldId}");
                        }
                        // Alt may be released BEFORE Win. Keep the early mask for search only;
                        // the definitive Win mask is immediately adjacent to the replacement Win-up.
                        if (decision.MaskMenu && modifiers.Alt) SendAltMenuMask();
                        QueueWheel(decision.Gesture, delta > 0);
                    }
                    return 1;
                }
            }
            catch (Exception) { /* never let a hook exception reach Windows */ }
        }
        return HotkeyNativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private nint KeyboardHookProc(int nCode, nint wParam, nint lParam)
    {
        bool swallow = false;
        try
        {
            if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN || wParam == WM_KEYUP || wParam == WM_SYSKEYUP))
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool down = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
                var observed = _keyboard.Observe((int)info.VirtualKey, down, info.ExtraInfo, info.ScanCode, (info.Flags & 1) != 0);
                if (QuickLookTrigger.IsCandidate((int)info.VirtualKey))
                {
                    var quickLook = Volatile.Read(ref _quickLook);
                    if (down && quickLook?.Enabled == true) _keyboard.ReconcileStale(IsDown);
                    // Key-up pairing is evaluated even after disabling/focus loss. Only candidate downs
                    // need native context, so ordinary typing has no extra native calls.
                    var context = down && quickLook?.Enabled == true
                        ? QuickLookNative.Context(quickLook, _keyboard.Modifiers) : default;
                    var decision = _quickLookTrigger.Observe((int)info.VirtualKey, down,
                        observed.OwnInput || (info.Flags & 0x10) != 0, context);
                    swallow = decision.Swallow;
                    if (down && quickLook?.Enabled == true && (info.Flags & 0x10) == 0)
                        TraceWinInput($"quicklook key=0x{info.VirtualKey:X2} command={decision.Command} swallowed={decision.Swallow} " +
                            $"foreground={context.ForegroundClass} focus={context.FocusClass} shellView={context.UnderShellView} ime={context.ImeComposing} modifiers={context.Modifiers}");
                    if (decision.Command != QuickLookCommand.None) quickLook?.Post(decision.Command, context.Foreground);
                }
                if (!observed.OwnInput)
                {
                    _wheel.ObserveWin(_keyboard.Modifiers.Win);
                    _wheel.ObserveAlt(_keyboard.Modifiers.Alt);
                    if (observed.WinKeyUp)
                    {
                        var result = WinKeyRelease.Process((ushort)info.VirtualKey, observed.GestureAccepted,
                            Volatile.Read(ref _stopping) != 0, SendKeys);
                        // Set before any diagnostics/queue work. No later exception can accidentally
                        // lose the successful replacement decision; failed injection forwards original up.
                        swallow = result.Swallow;
                        TraceWinInput($"hold={observed.HoldId} key=0x{info.VirtualKey:X2} accepted={observed.GestureAccepted} repeats={observed.Repeats} " +
                            $"upSwallowed={swallow} maskInjected={result.MaskInjected} SendInput={result.Inserted}/3 attempted={result.Attempted} " +
                            $"cleanup={result.CleanupInserted} failed={result.Failed} stopping={Volatile.Read(ref _stopping) != 0}");
                    }
                    if (observed.ResetSearch) Enqueue(() => _actions.Search.Reset());
                }
            }
        }
        catch (Exception) { /* fail open, unless a complete replacement was already inserted; never throw into Windows */ }
        return swallow ? 1 : HotkeyNativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void QueueWheel(WheelGesture gesture, bool up) => Enqueue(() =>
    {
        switch (gesture)
        {
            case WheelGesture.Resize: _actions.ResizeWindow(up, _cts.Token); break;
            case WheelGesture.Desktop: _actions.DesktopWheel(up, false, _cts.Token); break;
            case WheelGesture.MoveDesktop: _actions.DesktopWheel(up, true, _cts.Token); break;
            case WheelGesture.Search:
                if (!_actions.IsPaused && !_cts.IsCancellationRequested) _actions.Search.Wheel(up);
                break;
        }
    });

    private static bool IsDown(int virtualKey) => (HotkeyNativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void OnSessionChange(uint reason)
    {
        if (Volatile.Read(ref _stopping) != 0 || !_keyboard.ReseedForSessionChange(reason, IsDown)) return;
        // A session boundary starts fresh bookkeeping, even if the snapshot still includes Win.
        _wheel.ObserveWin(false);
        _wheel.ObserveAlt(false);
        Enqueue(() => _actions.Search.Reset());
        TraceWinInput($"resync=session reason=0x{reason:X} modifiers={_keyboard.Modifiers}");
    }

    private static void SendAltMenuMask()
    {
        SendKeys([new(VkUnassigned, false), new(VkUnassigned, true)]);
    }

    private static uint SendKeys(ShortcutKeyStroke[] keys)
    {
        var inputs = new INPUT[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            inputs[i] = new INPUT { Type = INPUT_KEYBOARD, Data = new INPUTUNION { Keyboard = new KEYBDINPUT {
                VirtualKey = keys[i].VirtualKey, ExtraInfo = keys[i].ExtraInfo,
                Flags = (keys[i].KeyUp ? KEYEVENTF_KEYUP : 0) | (keys[i].Extended ? KEYEVENTF_EXTENDEDKEY : 0),
            } } };
        return HotkeyNativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [Conditional("DEBUG")]
    private void TraceWinInput(string detail)
    {
#if DEBUG
        try
        {
            string? file = _inputDiagnosticLog;
            if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file)) return;
            string line = $"{DateTimeOffset.UtcNow:O} host={_inputDiagnosticHost} seq={++_inputDiagnosticSequence} {detail}{Environment.NewLine}";
            // Never do file I/O or take a file lock on the hook thread. Out-of-order writes retain seq.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (InputDiagnosticFileGate) File.AppendAllText(file, line); }
                catch (Exception) { /* optional diagnostics cannot affect input */ }
            });
        }
        catch (Exception) { }
#endif
    }

    private static void EnsureWindowClass()
    {
        if (_classRegistered) return;
        var wndClass = new WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProc),
            hInstance = HotkeyNativeMethods.GetModuleHandleW(null),
            lpszClassName = WindowClassName,
        };
        if (HotkeyNativeMethods.RegisterClassW(ref wndClass) == 0 &&
            Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
        {
            throw new InvalidOperationException("The window shortcut class could not be registered.");
        }
        _classRegistered = true;
    }

    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WM_WTSSESSION_CHANGE && _current is { } sessionHost)
        {
            try { sessionHost.OnSessionChange((uint)wParam); } catch (Exception) { /* never cross a native callback */ }
            return 0;
        }
        if (message == WM_HOTKEY && _current is { } host)
        {
            try { host.OnHotkey((int)wParam); } catch (Exception) { /* never cross a native callback */ }
            return 0;
        }
        return HotkeyNativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch (Exception) { }
        _multiPress.Fired -= OnMultiPressFired;
        _queue.Dispose();
        _cts.Dispose();
    }

    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);
    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public POINT Point;
        // Windows keeps a trailing DWORD here (lPrivate); it shares the struct's final padding on
        // x64. Declaring it keeps the size at 48 bytes and the layout identical to the SDK header.
        public uint LPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint VirtualKey, ScanCode, Flags, Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint Style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParamL;
        public ushort ParamH;
    }

    private static class HotkeyNativeMethods
    {
        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WTSUnRegisterSessionNotification(nint hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassW(ref WNDCLASSW wndClass);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
        [DllImport("user32.dll")] public static extern nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll")] public static extern int GetMessageW(out MSG message, nint hwnd, uint filterMin, uint filterMax);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG message);
        [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref MSG message);
        [DllImport("user32.dll")] public static extern bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
        [DllImport("user32.dll")] public static extern nint SetWindowsHookExW(int hook, HookProc callback, nint module, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string? name);
    }
}
