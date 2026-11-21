using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Diagnostics;
using FlowDesk.Core.Desktops;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.QuickLook;
using FlowDesk.Core.HotCorners;

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
    private const uint WM_CONFIGURE_HOTCORNERS = 0x800D;
    private const uint WM_CONFIGURE_BINDINGS = 0x800E;
    private const uint WM_CONFIGURE_WINDOWS_KEY = 0x8010;
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
    private readonly ShortcutRegistrations _registered = new();
    private readonly object _policyGate = new();
    private ShortcutPolicy _policy;
    private IReadOnlyList<HotkeyBinding> _bindings;
    private bool _desktopBindingsAvailable;
    private ShortcutRegistrationSnapshot _bindingStatus = ShortcutRegistrationSnapshot.Empty;
    private BindingChange? _bindingChange;
    private readonly HookProc _hookProc;
    private readonly HookProc _keyboardProc;
    private readonly WheelGestures _wheel = new();
    private readonly ShortcutKeyboardState _keyboard = new();
    private readonly ShortcutRepeatGate _repeatGate = new();
    private readonly HotkeyInputOwnership _inputOwnership = new();
    private readonly LoneWindowsKey _loneWindows = new();
    private bool _paletteWindowsKey;
    private bool _windowsMouseFailed;
    private WindowsKeyHookChange? _windowsKeyChange;
    private int _windowsKeyVersion, _seenWindowsKeyVersion;
    public void SetPaletteWindowsKey(bool enabled)
    {
        Volatile.Write(ref _paletteWindowsKey, enabled);
        Interlocked.Increment(ref _windowsKeyVersion);
    }
    private bool SoftwareRepeat => _windowShortcuts && _keyboardHook != 0;
    private QuickLookTrigger _quickLookTrigger = new();
    private bool _gameMode;
    private readonly GameModeInputBoundary _boundaryHeld = new();
    private IQuickLookInput? _quickLook;
    private IHotCornerInput? _hotCorners;
    private readonly object _hotCornerGate = new();
    private HotCornerHookChange? _hotCornerChange;
    private bool _sessionBlocked;
    private readonly bool _windowShortcuts;
#if DEBUG
    private readonly string? _inputDiagnosticLog = Environment.GetEnvironmentVariable("FLOWDESK_HOTKEY_DIAGNOSTICS_LOG");
    private readonly string _inputDiagnosticHost = Guid.NewGuid().ToString("N")[..8];
    private static readonly object InputDiagnosticFileGate = new();
    private long _inputDiagnosticSequence;
#endif
    private readonly Action? _openTerminal;
    private readonly Action? _reload;
    private readonly Action? _palette;
    private readonly Action? _clipboard;
    private readonly Action? _toggleGameMode;
    private Thread? _messageThread;
    private Thread? _workerThread;
    private uint _threadId;
    private nint _hwnd;
    private nint _hook;
    private nint _keyboardHook;
    private bool _sessionNotificationsRegistered;
    private int _stopping;

    public HotkeyHost(IWindowOps windows, IVirtualDesktops? desktops, Action<string>? notify,
        Action? openTerminal = null, Action? reload = null, bool windowShortcuts = true, IQuickLookInput? quickLook = null,
        IHotCornerInput? hotCorners = null, IEnumerable<string>? disabledShortcutIds = null, Action? palette = null, Action? clipboard = null,
        IReadOnlyDictionary<string, string>? shortcutOverrides = null, Action? toggleGameMode = null, bool gameMode = false)
    {
        _desktops = desktops;
        _notify = notify;
        _actions = new WindowShortcutActions(windows, desktops, new CancellableDelay());
        _hookProc = MouseHookProc;
        _keyboardProc = KeyboardHookProc;
        _openTerminal = openTerminal;
        _reload = reload;
        _palette = palette;
        _clipboard = clipboard;
        _toggleGameMode = toggleGameMode;
        _gameMode = gameMode;
        _windowShortcuts = windowShortcuts;
        _quickLook = quickLook;
        _hotCorners = hotCorners;
        _policy = new(disabledShortcutIds);
        _bindings = ShortcutRebinding.Effective(shortcutOverrides);
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
    internal bool KeyboardHookRequired => InputHookRequirements.For(_windowShortcuts, _quickLook is not null,
        _hotCorners is not null, _palette is not null || _clipboard is not null, _paletteWindowsKey).Keyboard;
    public bool PaletteWindowsKeyAvailable => KeyboardHookAvailable && !Volatile.Read(ref _windowsMouseFailed);
    public void SetQuickLook(IQuickLookInput? input) => Volatile.Write(ref _quickLook, input);
    public ShortcutRegistrationSnapshot BindingStatus => Volatile.Read(ref _bindingStatus);
    private sealed record BindingChange(ShortcutPolicy Policy, IReadOnlyList<HotkeyBinding> Bindings, bool Atomic, bool GameMode)
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public async Task ConfigureBindingsAsync(IEnumerable<string> disabledIds, IReadOnlyDictionary<string, string>? overrides = null, bool atomic = false, bool? gameMode = null)
    {
        var change = new BindingChange(new(disabledIds), ShortcutRebinding.Effective(overrides), atomic, gameMode ?? _gameMode);
        lock (_hotCornerGate)
        {
            if (Volatile.Read(ref _stopping) != 0 || _bindingChange is not null) throw new InvalidOperationException("Input host is stopping or changing.");
            _bindingChange = change;
            if (!HotkeyNativeMethods.PostMessageW(_hwnd, WM_CONFIGURE_BINDINGS, 0, 0))
            { _bindingChange = null; throw new InvalidOperationException("Shortcut update could not be queued."); }
        }
        try { await change.Completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_hotCornerGate) { if (ReferenceEquals(_bindingChange, change)) _bindingChange = null; }
            if (change.Completion.Task.IsCompleted) { await change.Completion.Task.ConfigureAwait(false); return; }
            throw;
        }
    }
    private void ApplyBindingChange()
    {
        lock (_hotCornerGate)
        {
            var change = _bindingChange; _bindingChange = null;
            if (change is null) return;
            try
            {
                if (Volatile.Read(ref _stopping) != 0) throw new InvalidOperationException("Input host stopped.");
                var effective = change.Policy.ForModules(_windowShortcuts, _palette is not null, _clipboard is not null, change.GameMode);
                if (!SoftwareRepeat && change.Bindings.Where(b => effective.CanRegister(b, _desktops?.IsAvailable == true)).Any(b => _registered.NativeIds.Any(id => _registered.TryGet(id, out var held) &&
                    KeyChord.From(held) == KeyChord.From(b) && held.Repeat != b.Repeat)))
                    throw new InvalidOperationException("This swap needs the keyboard hook, which is unavailable. Your shortcuts were kept.");
                var required = change.Bindings.Where(b => KeyChord.From(b) != KeyChord.From(_bindings.Single(old => old.Id == b.Id)) ||
                    (change.Policy.Enabled(b.Id) && !_policy.Enabled(b.Id))).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
                var snapshot = _registered.Apply(effective, _desktops?.IsAvailable == true, new NativeRegistrar(this), change.Bindings, change.Atomic, required);
                if (_gameMode != change.GameMode)
                {
                    // Mask the already-forwarded Win/Alt hold before clearing ownership. Never inject
                    // modifier ups: the user's physical ups must still reach the foreground application.
                    if (_keyboard.Modifiers.Win || _keyboard.Modifiers.Alt) SendAltMenuMask();
                    _boundaryHeld.Seed(IsDown);
                    _keyboard.Seed(IsDown); _repeatGate.Reset(); _inputOwnership.Reset();
                    _loneWindows.Reset(); _quickLookTrigger = new();
                    Volatile.Write(ref _gameMode, change.GameMode);
                    _hotCorners?.SetHookAvailable(!change.GameMode && _hook != 0 && _sessionNotificationsRegistered);
                }
                while (HotkeyNativeMethods.PeekMessageW(out _, _hwnd, WM_HOTKEY, WM_HOTKEY, 1)) { }
                lock (_policyGate) { Volatile.Write(ref _policy, change.Policy); _bindings = change.Bindings; _multiPress.Reset(); }
                Volatile.Write(ref _bindingStatus, snapshot);
                Volatile.Write(ref _desktopBindingsAvailable, _desktops?.IsAvailable == true);
                UpdateConflictText(snapshot);
                _wheel.Reset();
                Enqueue(() => _actions.Search.Reset());
                change.Completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _bindingStatus, BindingStatus with { Registered = _registered.RegisteredIds });
                change.Completion.TrySetException(ex);
            }
        }
    }

    private sealed record HotCornerHookChange(IHotCornerInput? Input)
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public async Task ConfigureHotCornersAsync(IHotCornerInput? input)
    {
        var change = new HotCornerHookChange(input);
        lock (_hotCornerGate)
        {
            if (Volatile.Read(ref _stopping) != 0 || _hotCornerChange is not null) throw new InvalidOperationException("Input host is stopping or changing.");
            _hotCornerChange = change;
            if (!HotkeyNativeMethods.PostMessageW(_hwnd, WM_CONFIGURE_HOTCORNERS, 0, 0))
            { _hotCornerChange = null; throw new InvalidOperationException("Mouse hook update could not be queued."); }
        }
        try { await change.Completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_hotCornerGate)
            {
                if (ReferenceEquals(_hotCornerChange, change)) _hotCornerChange = null; // queued update must not apply late
            }
            if (change.Completion.Task.IsCompleted) { await change.Completion.Task.ConfigureAwait(false); return; }
            throw;
        }
    }
    private void ApplyHotCornerHookChange()
    {
        lock (_hotCornerGate)
        {
            var change = _hotCornerChange; _hotCornerChange = null;
            if (change is null) return;
            try
            {
                if (Volatile.Read(ref _stopping) != 0) throw new InvalidOperationException("Input host is stopping.");
                if (change.Input is not null)
                {
                    if (!_sessionNotificationsRegistered) throw new InvalidOperationException("Session safety notifications are unavailable.");
                    if (_hook == 0) _hook = HotkeyNativeMethods.SetWindowsHookExW(WH_MOUSE_LL, _hookProc, HotkeyNativeMethods.GetModuleHandleW(null), 0);
                    if (_hook == 0) throw new InvalidOperationException("Mouse hook could not be installed.");
                }
                var previous = Volatile.Read(ref _hotCorners);
                if (!ReferenceEquals(previous, change.Input)) previous?.SetHookAvailable(false);
                Volatile.Write(ref _hotCorners, change.Input);
                change.Input?.SetSessionBlocked(_sessionBlocked);
                change.Input?.SetHookAvailable(!_gameMode);
                ReleaseUnusedMouseHook();
                change.Completion.TrySetResult(true);
            }
            catch (Exception ex) { change.Completion.TrySetException(ex); }
        }
    }

    private sealed record WindowsKeyHookChange(bool Enabled)
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public async Task<bool> ConfigurePaletteWindowsKeyAsync(bool enabled)
    {
        var change = new WindowsKeyHookChange(enabled);
        lock (_hotCornerGate)
        {
            if (Volatile.Read(ref _stopping) != 0 || _windowsKeyChange is not null) throw new InvalidOperationException("Input host is stopping or changing.");
            _windowsKeyChange = change;
            if (!HotkeyNativeMethods.PostMessageW(_hwnd, WM_CONFIGURE_WINDOWS_KEY, 0, 0))
            { _windowsKeyChange = null; throw new InvalidOperationException("Windows-key update could not be queued."); }
        }
        try { return await change.Completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_hotCornerGate) { if (ReferenceEquals(_windowsKeyChange, change)) _windowsKeyChange = null; }
            if (change.Completion.Task.IsCompleted) return await change.Completion.Task.ConfigureAwait(false);
            throw;
        }
    }
    private void ApplyWindowsKeyHookChange()
    {
        lock (_hotCornerGate)
        {
            var change = _windowsKeyChange; _windowsKeyChange = null;
            if (change is null) return;
            try
            {
                if (Volatile.Read(ref _stopping) != 0) throw new InvalidOperationException("Input host stopped.");
                if (change.Enabled && _keyboardHook == 0)
                { LogHookUnavailable("Windows-key keyboard hook"); change.Completion.TrySetResult(false); return; }
                if (change.Enabled && _hook == 0)
                {
                    _hook = HotkeyNativeMethods.SetWindowsHookExW(WH_MOUSE_LL, _hookProc, HotkeyNativeMethods.GetModuleHandleW(null), 0);
                    Volatile.Write(ref _windowsMouseFailed, _hook == 0);
                    if (_hook == 0) { LogHookUnavailable("Windows-key mouse hook"); change.Completion.TrySetResult(false); return; }
                }
                SetPaletteWindowsKey(change.Enabled);
                _loneWindows.Reset();
                ReleaseUnusedMouseHook();
                change.Completion.TrySetResult(true);
            }
            catch (Exception ex) { change.Completion.TrySetException(ex); }
        }
    }
    private void ReleaseUnusedMouseHook()
    {
        bool needed = InputHookRequirements.For(_windowShortcuts, _quickLook is not null, _hotCorners is not null,
            _palette is not null || _clipboard is not null, _paletteWindowsKey).Mouse;
        if (!needed && _hook != 0 && HotkeyNativeMethods.UnhookWindowsHookEx(_hook)) _hook = 0;
        WheelHookAvailable = _hook != 0;
        WinWheelHookAvailable = _hook != 0 && _keyboardHook != 0;
    }
    private void LogHookUnavailable(string feature)
    {
        TraceWinInput($"hook-unavailable feature={feature} native-hotkeys-retained=true");
        new CrashLog().TryWrite(CrashSource.Activation, new InvalidOperationException(feature + " is unavailable; native hotkeys remain enabled."));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_messageThread is not null) throw new InvalidOperationException("The hotkey host is already started.");
        _multiPress.FiredWithContext += OnMultiPressFired;
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
            _multiPress.FiredWithContext -= OnMultiPressFired;
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

            _sessionNotificationsRegistered = HotkeyNativeMethods.WTSRegisterSessionNotification(_hwnd, 0); // this session
            TraceWinInput($"session-notifications registered={_sessionNotificationsRegistered} error={(_sessionNotificationsRegistered ? 0 : Marshal.GetLastWin32Error())}");
            _keyboard.Seed(IsDown);
            bool paletteInput = _palette is not null || _clipboard is not null;
            var requirements = InputHookRequirements.For(_windowShortcuts, _quickLook is not null, _hotCorners is not null, paletteInput, _paletteWindowsKey);
            if (requirements.Mouse) _hook = HotkeyNativeMethods.SetWindowsHookExW(WH_MOUSE_LL, _hookProc, HotkeyNativeMethods.GetModuleHandleW(null), 0);
            if (requirements.Keyboard) _keyboardHook = HotkeyNativeMethods.SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, HotkeyNativeMethods.GetModuleHandleW(null), 0);
            if (requirements.Keyboard && _keyboardHook == 0) LogHookUnavailable("Keyboard hook (release protection off)");
            if (requirements.Mouse && _hook == 0) LogHookUnavailable("Mouse hook");
            _windowsMouseFailed = _paletteWindowsKey && _hook == 0;
            if (!paletteInput && _quickLook is not null && _keyboardHook == 0) throw new InvalidOperationException("Quick Look keyboard hook could not be installed.");
            if (!paletteInput && _hotCorners is not null && (_hook == 0 || !_sessionNotificationsRegistered)) throw new InvalidOperationException("Hot corner mouse/session safety hooks are unavailable.");
            _hotCorners?.SetSessionBlocked(_sessionBlocked);
            _hotCorners?.SetHookAvailable(!_gameMode && _hook != 0 && _sessionNotificationsRegistered);
            WheelHookAvailable = _hook != 0;
            WinWheelHookAvailable = _hook != 0 && _keyboardHook != 0;
            RegisterBindings();
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
            _hotCorners?.SetHookAvailable(false);
            lock (_hotCornerGate)
            {
                _hotCornerChange?.Completion.TrySetException(new InvalidOperationException("Input host stopped."));
                _hotCornerChange = null;
                _bindingChange?.Completion.TrySetException(new InvalidOperationException("Input host stopped."));
                _bindingChange = null;
                _windowsKeyChange?.Completion.TrySetException(new InvalidOperationException("Input host stopped."));
                _windowsKeyChange = null;
            }
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
            _registered.Clear(new NativeRegistrar(this));
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
        var policy = Volatile.Read(ref _policy);
        bool desktopAvailable = _desktops?.IsAvailable == true;
        Volatile.Write(ref _desktopBindingsAvailable, desktopAvailable);
        var effective = policy.ForModules(_windowShortcuts, _palette is not null, _clipboard is not null, _gameMode);
        var snapshot = _registered.Apply(effective,
            desktopAvailable, new NativeRegistrar(this), _bindings);
        Volatile.Write(ref _bindingStatus, snapshot);
        UpdateConflictText(snapshot);
    }
    private void UpdateConflictText(ShortcutRegistrationSnapshot snapshot)
    {
        lock (_conflicts)
        {
            _conflicts.Clear();
            _conflicts.AddRange(_bindings.Where(b => snapshot.Conflicts.Contains(b.Id)).Select(b => b.Shortcut));
        }
    }
    private sealed class NativeRegistrar(HotkeyHost host) : IShortcutRegistrar
    {
        public ShortcutRegistrationResult Register(int id, HotkeyBinding binding)
        {
            uint modifiers = binding.Modifiers | (binding.Repeat || host.SoftwareRepeat ? 0u : ModNoRepeat);
            if (HotkeyNativeMethods.RegisterHotKey(host._hwnd, id, modifiers, binding.VirtualKey)) return ShortcutRegistrationResult.Active;
            return Marshal.GetLastWin32Error() == 1409 ? ShortcutRegistrationResult.Conflict : ShortcutRegistrationResult.Unavailable;
        }
        public bool Unregister(int id) => HotkeyNativeMethods.UnregisterHotKey(host._hwnd, id) || Marshal.GetLastWin32Error() == 1419;
    }

    private void OnHotkey(int id)
    {
        if (!_registered.TryGet(id, out var binding)) return;
        if (_keyboardHook != 0 && _boundaryHeld.Contains(binding.VirtualKey)) return; // no auto-repeat may cross a mode boundary
        var policy = Volatile.Read(ref _policy);
        if (!policy.Enabled(binding.Id)) return;
        if (_gameMode && !GameModeRules.Retained(binding.Id)) return;
        if (SoftwareRepeat && !_repeatGate.Accept(binding)) return;
        // Native WM_HOTKEY grants foreground permission. Never replace it with a posted private message.
        TraceWinInput($"native-hotkey id={binding.Id}");
        if (binding.Id == HotkeyCatalog.PaletteId) { _palette?.Invoke(); return; }
        if (binding.Id == HotkeyCatalog.GameModeToggleId) { _toggleGameMode?.Invoke(); return; }
        if (binding.Id == HotkeyCatalog.ClipboardId) { _clipboard?.Invoke(); return; }
        SendAltMenuMask();
        Enqueue(() => HandleBinding(binding, policy));
    }

    internal bool IsPaused => _actions.IsPaused;
    public void TogglePause() => Enqueue(() => { if (!_windowShortcuts) return; _actions.SetPaused(!_actions.IsPaused); _multiPress.Reset(); });

    internal void HandleBinding(HotkeyBinding binding) => HandleBinding(binding, Volatile.Read(ref _policy));
    private void HandleBinding(HotkeyBinding binding, ShortcutPolicy policy)
    {
        if (!_windowShortcuts || Volatile.Read(ref _gameMode)) return;
        lock (_policyGate)
        {
            if (!policy.CanExecute(Volatile.Read(ref _policy), binding.Id)) return;
            if (HotkeyCatalog.TryGetFocusDirection(binding, out var direction))
            {
                if (!_actions.IsPaused) _multiPress.Press(direction, policy);
                return;
            }
        }
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
        _actions.Execute(binding, _cts.Token);
    }

    private void OnMultiPressFired(WindowDirection direction, int steps, object? context) =>
        Enqueue(() =>
        {
            if (!Volatile.Read(ref _gameMode) && context is ShortcutPolicy policy && policy.IsCurrent(Volatile.Read(ref _policy)) && !_actions.IsPaused) _actions.Focus(direction, steps, _cts.Token);
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
        if (Volatile.Read(ref _gameMode))
        {
            if (nCode >= 0 && _paletteWindowsKey)
            {
                int message = (int)wParam;
                _loneWindows.ObserveMouse(message is 0x201 or 0x204 or 0x207 or 0x20B,
                    message is 0x202 or 0x205 or 0x208 or 0x20C, message is 0x20A or 0x20E);
            }
            return HotkeyNativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        if (nCode >= 0 && Volatile.Read(ref _stopping) == 0)
        {
            int message = (int)wParam;
            // Moves do not disqualify a tap; every button and either wheel does.
            _loneWindows.ObserveMouse(message is 0x201 or 0x204 or 0x207 or 0x20B,
                message is 0x202 or 0x205 or 0x208 or 0x20C, message is 0x20A or 0x20E);
        }
        if (nCode >= 0 && Volatile.Read(ref _stopping) == 0 && Volatile.Read(ref _hotCorners) is { } corners)
        {
            try
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                corners.Observe(info.Point.X, info.Point.Y, (int)wParam, info.MouseData);
            }
            catch (Exception) { /* hook records only; never let exceptions reach Windows */ }
        }
        if (_windowShortcuts && nCode >= 0 && wParam == WM_MOUSEWHEEL && Volatile.Read(ref _stopping) == 0)
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
                var policy = Volatile.Read(ref _policy);
                if (!policy.AllowsWheel(WheelGestures.Route(modifiers.Win, modifiers.Alt, modifiers.Shift, modifiers.Control), _desktopBindingsAvailable))
                    return HotkeyNativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                var decision = _wheel.Accept(modifiers.Win, modifiers.Alt, modifiers.Shift, modifiers.Control, Environment.TickCount64, delta);
                if (decision.Gesture != WheelGesture.None)
                {
                    if (modifiers.Win)
                    {
                        // Partial high-resolution swipes are swallowed too: Win-up must not open Start
                        // just because no full notch was accumulated before the user released Win.
                        _keyboard.AcceptWinGesture();
                        if (decision.Accepted || decision.MaskMenu)
                            TraceWinInput($"gesture={decision.Gesture} accepted={decision.Accepted} leftHold={_keyboard.LeftHoldId} rightHold={_keyboard.RightHoldId}");
                    }
                    // Alt may be released BEFORE Win. Keep the early mask for search only;
                    // the definitive Win mask is immediately adjacent to the replacement Win-up.
                    if (decision.MaskMenu && modifiers.Alt) SendAltMenuMask();
                    if (decision.Accepted) QueueWheel(decision.Gesture, delta > 0, policy);
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
                _repeatGate.Observe(info.VirtualKey, down);
                var observed = _keyboard.Observe((int)info.VirtualKey, down, info.ExtraInfo, info.ScanCode, (info.Flags & 1) != 0);
                bool boundary = _boundaryHeld.Observe(info.VirtualKey, down);
                if (boundary || GameModeRules.PassHooks(_gameMode, _paletteWindowsKey))
                    return HotkeyNativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                if (!observed.OwnInput && Volatile.Read(ref _stopping) == 0)
                {
                    int version = Volatile.Read(ref _windowsKeyVersion);
                    if (version != _seenWindowsKeyVersion) { _loneWindows.Reset(); _seenWindowsKeyVersion = version; }
                    var modifiers = _keyboard.Modifiers;
                    bool loneSetting = Volatile.Read(ref _paletteWindowsKey);
                    bool lonePolicy = Volatile.Read(ref _policy).Enabled(HotkeyCatalog.PaletteId);
                    bool lonePaused = !_gameMode && _actions.IsPaused;
                    bool loneEnabled = loneSetting && _hook != 0 && _keyboardHook != 0 && _palette is not null &&
                        !lonePaused && !_sessionBlocked && lonePolicy;
                    bool loneCandidate = _loneWindows.IsCandidate;
                    bool sampleButtons = loneEnabled && down && info.VirtualKey is 0x5B or 0x5C;
                    bool loneTap = _loneWindows.ObserveKey((int)info.VirtualKey, down, Environment.TickCount64, loneEnabled,
                        modifiers.Alt || modifiers.Control || modifiers.Shift || (sampleButtons && (IsDown(1) || IsDown(2) || IsDown(4) || IsDown(5) || IsDown(6))));
                    if (loneTap)
                    {
                        var masked = WinKeyRelease.Process((ushort)info.VirtualKey, true, false, SendKeys);
                        swallow = masked.Swallow;
                        if (masked.Swallow) _palette?.Invoke(); // only open when Start suppression succeeded
                    }
                    if (info.VirtualKey is 0x5B or 0x5C || loneCandidate)
                        TraceWinInput($"lone-win key=0x{info.VirtualKey:X2} down={down} enabled={loneEnabled} setting={loneSetting} " +
                            $"mouseHook={_hook != 0} keyboardHook={_keyboardHook != 0} callback={_palette is not null} paused={lonePaused} " +
                            $"sessionBlocked={_sessionBlocked} policy={lonePolicy} candidateBefore={loneCandidate} candidate={_loneWindows.IsCandidate} " +
                            $"tap={loneTap} masked.Swallow={swallow} injected={(info.Flags & 0x10) != 0} modifiers={modifiers}");
                    uint mods = (modifiers.Win ? 8u : 0) | (modifiers.Alt ? 1u : 0) | (modifiers.Control ? 2u : 0) | (modifiers.Shift ? 4u : 0);
                    HotkeyBinding? matched = null;
                    if (down && !KeyChord.IsModifier(info.VirtualKey))
                        foreach (int id in _registered.NativeIds)
                            if (_registered.TryGet(id, out var binding) && binding.VirtualKey == info.VirtualKey && binding.Modifiers == mods)
                            { matched = binding; break; }
                    var owned = _gameMode ? default : _inputOwnership.Observe(info.VirtualKey, down, Environment.TickCount64, matched);
                    swallow |= owned.Swallow;
                    if (owned.Swallow || owned.Armed) TraceWinInput($"paired-trigger key=0x{info.VirtualKey:X2} down={down} swallowed={owned.Swallow}");
                    if (owned.Armed)
                    {
                        _keyboard.AcceptWinGesture();
                        if (modifiers.Alt) SendAltMenuMask();
                    }
                    // Modifier downs were forwarded: forwarding their balanced ups is essential.
                    // An adjacent vkE8 mask prevents Alt-up opening the previous application's menu.
                    if (_inputOwnership.TakeAltMask(info.VirtualKey, down, Environment.TickCount64)) SendAltMenuMask();
                }
                if (!_gameMode && QuickLookTrigger.IsCandidate((int)info.VirtualKey))
                {
                    var quickLook = Volatile.Read(ref _quickLook);
                    if (down && quickLook?.Enabled == true) _keyboard.ReconcileStale(IsDown);
                    // Key-up pairing is evaluated even after disabling/focus loss. Only candidate downs
                    // need native context, so ordinary typing has no extra native calls.
                    var context = down && quickLook?.Enabled == true
                        ? QuickLookNative.Context(quickLook, _keyboard.Modifiers) : default;
                    var decision = _quickLookTrigger.Observe((int)info.VirtualKey, down,
                        observed.OwnInput, context);
                    swallow |= decision.Swallow;
                    if (down && quickLook?.Enabled == true && !observed.OwnInput)
                        TraceWinInput($"quicklook key=0x{info.VirtualKey:X2} command={decision.Command} swallowed={decision.Swallow} " +
                            $"foreground={context.ForegroundClass} focus={context.FocusClass} shellView={context.UnderShellView} ime={context.ImeComposing} injected={(info.Flags & 0x10) != 0} modifiers={context.Modifiers}");
                    if (decision.Command != QuickLookCommand.None) quickLook?.Post(decision.Command, context.Foreground);
                }
                if (!observed.OwnInput)
                {
                    _wheel.ObserveWin(_keyboard.Modifiers.Win);
                    _wheel.ObserveAlt(_keyboard.Modifiers.Alt);
                    if (!_gameMode && observed.WinKeyUp && !swallow)
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

    private void QueueWheel(WheelGesture gesture, bool up, ShortcutPolicy policy) => Enqueue(() =>
    {
        if (Volatile.Read(ref _gameMode) || !policy.IsCurrent(Volatile.Read(ref _policy)) || !policy.AllowsWheel(gesture, Volatile.Read(ref _desktopBindingsAvailable))) return;
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
        _boundaryHeld.Seed(IsDown); // lost key-ups on lock/disconnect must not strand a retained shortcut
        _repeatGate.Reset();
        _inputOwnership.Reset();
        _loneWindows.Reset();
        if (reason is 2 or 4 or 6 or 7) { _sessionBlocked = true; _hotCorners?.SetSessionBlocked(true); }
        else if (reason is 1 or 3 or 5 or 8 or 0xF) { _sessionBlocked = false; _hotCorners?.SetSessionBlocked(false); }
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
        if (message == WM_CONFIGURE_BINDINGS && _current is { } bindingHost)
        { try { bindingHost.ApplyBindingChange(); } catch (Exception) { } return 0; }
        if (message == WM_CONFIGURE_HOTCORNERS && _current is { } cornerHost)
        { try { cornerHost.ApplyHotCornerHookChange(); } catch (Exception) { } return 0; }
        if (message == WM_CONFIGURE_WINDOWS_KEY && _current is { } windowsKeyHost)
        { try { windowsKeyHost.ApplyWindowsKeyHookChange(); } catch (Exception) { } return 0; }
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
        _multiPress.FiredWithContext -= OnMultiPressFired;
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
        [DllImport("user32.dll", SetLastError = true)] public static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
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
        [DllImport("user32.dll")] public static extern bool PeekMessageW(out MSG message, nint hwnd, uint filterMin, uint filterMax, uint remove);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG message);
        [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref MSG message);
        [DllImport("user32.dll")] public static extern bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(nint hwnd, int id);
        [DllImport("user32.dll")] public static extern nint SetWindowsHookExW(int hook, HookProc callback, nint module, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string? name);
    }
}
