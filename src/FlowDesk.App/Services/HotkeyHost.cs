using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FlowDesk.Core.Desktops;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Services;

/// <summary>
/// Owns global window shortcuts for as long as the feature is on. One dedicated thread runs the
/// message-only window, the RegisterHotKey ids and the low-level mouse hook; that thread only checks
/// keys/cooldown and queues. One serialized worker thread runs every action and the timed focus step,
/// so actions never overlap. Stop unregisters, unhooks, joins and drains stale work.
/// </summary>
public sealed class HotkeyHost : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_QUIT = 0x0012;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WH_MOUSE_LL = 14;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const int WheelCooldownMs = 25;
    private const uint ModNoRepeat = 0x4000;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
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
    private Thread? _messageThread;
    private Thread? _workerThread;
    private uint _threadId;
    private nint _hwnd;
    private nint _hook;
    private long _lastWheelTick;
    private int _stopping;

    public HotkeyHost(IWindowOps windows, IVirtualDesktops? desktops, Action<string>? notify)
    {
        _desktops = desktops;
        _notify = notify;
        _actions = new WindowShortcutActions(windows, desktops, new CancellableDelay());
        _hookProc = MouseHookProc;
    }

    public bool IsRunning { get; private set; }

    /// <summary>Shortcuts Windows refused because another app already owns them.</summary>
    public IReadOnlyList<string> Conflicts
    {
        get { lock (_conflicts) return _conflicts.ToArray(); }
    }

    public bool WheelHookAvailable { get; private set; }

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

            RegisterBindings();
            _hook = HotkeyNativeMethods.SetWindowsHookExW(WH_MOUSE_LL, _hookProc, HotkeyNativeMethods.GetModuleHandleW(null), 0);
            WheelHookAvailable = _hook != 0;
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
            if (_hook != 0)
            {
                HotkeyNativeMethods.UnhookWindowsHookEx(_hook);
                _hook = 0;
            }
            foreach (int id in _registered.Keys) HotkeyNativeMethods.UnregisterHotKey(_hwnd, id);
            _registered.Clear();
            if (_hwnd != 0)
            {
                HotkeyNativeMethods.DestroyWindow(_hwnd);
                _hwnd = 0;
            }
            if (ReferenceEquals(_current, this)) _current = null;
            IsRunning = false;
            WheelHookAvailable = false;
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

    private void HandleBinding(HotkeyBinding binding)
    {
        if (binding.Id == HotkeyCatalog.PauseToggleId)
        {
            bool paused = _actions.TogglePause();
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
                if (IsDown(VK_MENU) && IsDown(VK_SHIFT) && !IsDown(VK_CONTROL) && !IsDown(VK_LWIN) && !IsDown(VK_RWIN))
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    int delta = (short)((info.MouseData >> 16) & 0xFFFF);
                    if (delta != 0) QueueWheel(delta > 0);
                    return 1;
                }
            }
            catch (Exception) { /* never let a hook exception reach Windows */ }
        }
        return HotkeyNativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void QueueWheel(bool grow)
    {
        long now = Environment.TickCount64;
        if (now - _lastWheelTick < WheelCooldownMs) return;
        _lastWheelTick = now;
        Enqueue(() => _actions.ResizeWindow(grow, _cts.Token));
    }

    private static bool IsDown(int virtualKey) => (HotkeyNativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static void SendAltMenuMask()
    {
        var inputs = new INPUT[2];
        inputs[0] = new INPUT { Type = INPUT_KEYBOARD, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { VirtualKey = VkUnassigned } } };
        inputs[1] = new INPUT { Type = INPUT_KEYBOARD, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { VirtualKey = VkUnassigned, Flags = KEYEVENTF_KEYUP } } };
        HotkeyNativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
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
        if (message == WM_HOTKEY && _current is { } host)
        {
            host.OnHotkey((int)wParam);
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
