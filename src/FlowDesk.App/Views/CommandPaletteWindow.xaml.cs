using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowDesk.App.Services;
using FlowDesk.Core.Palette;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;

namespace FlowDesk.App.Views;

internal sealed partial class PaletteResultRow(CommandMatch match) : ObservableObject
{
    public CommandMatch Match => match;
    public string Title => match.Item.Title;
    public string Subtitle => match.Item.Subtitle;
    public string AutomationName => Title + ", " + Subtitle;
    public string Glyph => match.Item.Icon.Length == 1 ? match.Item.Icon : match.Item.Kind == CommandKind.App ? "\uE71D" : "\uE8A5";
    [ObservableProperty] public partial ImageSource? IconSource { get; set; }
    public Visibility IconFallbackVisibility => IconSource is null ? Visibility.Visible : Visibility.Collapsed;
    partial void OnIconSourceChanged(ImageSource? value) => OnPropertyChanged(nameof(IconFallbackVisibility));
    [ObservableProperty] public partial string Hint { get; set; } = "";
}

internal sealed partial class CommandPaletteWindow : Window
{
    private readonly CommandSearch search;
    private readonly PaletteUsageStore usage;
    private readonly Action<string> notify;
    private readonly QuickLookShell icons = new();
    private readonly PaletteAcrylicBackdrop acrylic;
    private readonly nint hwnd;
    private readonly Dictionary<string, ImageSource> iconCache = [];
    private PaletteRect workArea = new(0, 0, 640, 540);
    private double monitorScale = 1;
    private CancellationTokenSource? query;
    private long generation;
    private long openEpoch;
    private long openStarted;
    private bool shutdown, opening, running, activationPending;
    public bool IsOpen { get; private set; }
    public Action? BeforeQuery { get; set; }
    public nint Handle => hwnd;
    public PaletteMode Mode { get; private set; }
    public Func<bool>? ClipboardEnabled { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<CommandMatch>>>? ClipboardQuery { get; set; }
    public Action? ClearClipboard { get; set; }
    public static nint Foreground => GetForegroundWindow();
    public static uint ProcessOf(nint window) { GetWindowThreadProcessId(window, out uint process); return process; }
    public static string ClassOf(nint window) { var text = new StringBuilder(256); GetClassNameW(window, text, text.Capacity); return text.ToString(); }
    public CommandPaletteWindow(CommandSearch search, PaletteUsageStore usage, Action<string> notify)
    {
        this.search = search; this.usage = usage; this.notify = notify;
        InitializeComponent();
        Title = "Command palette — FlowDesk";
        acrylic = new PaletteAcrylicBackdrop();
        acrylic.StateChanged += UpdateBackdropSurface;
        SystemBackdrop = acrylic;
        acrylic.Update(false, Root.ActualTheme);
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        TraceSurface("before-presenter");
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false); p.IsResizable = false; p.IsMaximizable = false; p.IsMinimizable = false; p.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;
        TraceSurface("after-presenter");
        EnsureBorderlessFrame();
        SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | 0x80); // TOOLWINDOW, deliberately NOT NOACTIVATE
        int round = 2; int roundResult = DwmSetWindowAttribute(hwnd, 33, ref round, 4);
        // The native frame is removed above; the theme-aware XAML stroke owns the outline.
        int noNativeBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE, Windows 11+
        int borderResult = DwmSetWindowAttribute(hwnd, 34, ref noNativeBorder, 4); // DWMWA_BORDER_COLOR
        TraceSurface($"attributes-set roundHR=0x{roundResult:X8} borderHR=0x{borderResult:X8}");
        Root.ActualThemeChanged += (_, _) => { acrylic.Update(IsOpen, Root.ActualTheme); TraceSurface("theme-changed"); };
        AppWindow.Resize(new SizeInt32(640, 540));
        EnsureBorderlessFrame();
        AppWindow.Closing += (_, e) => { if (!shutdown) { e.Cancel = true; HidePalette(); } };
        Activated += (_, e) =>
        {
            TraceSurface($"activated-{e.WindowActivationState}");
#if DEBUG
            // Configuration updates can run after Window.Activated; capture the settled value too.
            DispatcherQueue.TryEnqueue(() => TraceSurface("activation-settled"));
#endif
            if (!opening && !activationPending && IsOpen && e.WindowActivationState == WindowActivationState.Deactivated) HidePalette();
        };
        Root.Loaded += (_, _) => TraceSurface("root-loaded");
        SearchBox.Loaded += (_, _) =>
        {
            if (IsOpen && GetForegroundWindow() == hwnd && SearchBox.Focus(FocusState.Programmatic)) TraceOpen(openStarted, true);
        };
        SearchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Back && ClipboardModeRules.Backspace(Mode, SearchBox.Text) != Mode)
            { e.Handled = true; SetMode(PaletteMode.Commands); }
        };
        Results.ContainerContentChanging += (_, e) =>
        {
            if (!e.InRecycleQueue && e.Item is PaletteResultRow row) AutomationProperties.SetName(e.ItemContainer, row.AutomationName);
        };
        AppWindow.Hide();
        TraceSurface("constructed-hidden");
    }
    public void Open(nint previous, string theme, long started, PaletteMode mode = PaletteMode.Commands)
    {
        if (shutdown || running) return;
        opening = true;
        activationPending = true;
        openStarted = started;
        long epoch = ++openEpoch;
        try
        {
            Root.RequestedTheme = theme switch { "Dark" => ElementTheme.Dark, "Light" => ElementTheme.Light, _ => ElementTheme.Default };
            TraceSurface("opening");
            nint monitor = previous == 0 ? CursorMonitor() : MonitorFromWindow(previous, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(monitor, ref info)) throw new InvalidOperationException("Display unavailable.");
            double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) >= 0 ? dpi / 96.0 : 1;
            workArea = new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
            monitorScale = scale;
            ResizeForResults(0);
            IsOpen = true; SetMode(mode); SearchBox.Text = ""; Results.ItemsSource = null; SetStatus(mode == PaletteMode.Clipboard ? "Clipboard" : "Recent");
            acrylic.Update(true, Root.ActualTheme);
            AppWindow.Show(); Activate();
            EnsureBorderlessFrame();
            TraceSurface("shown");
            // No synthetic keys or AttachThreadInput: use the foreground permission granted by WM_HOTKEY.
            bool foreground = SetForegroundWindow(hwnd) || GetForegroundWindow() == hwnd;
            bool focused = SearchBox.Focus(FocusState.Programmatic);
            if (!foreground || !focused) _ = RetryFocusAsync(epoch, previous, started);
            else { activationPending = false; TraceOpen(started, true); }
            QueueQuery();
        }
        catch (Exception ex) { Log(ex); HidePalette(); notify("Command palette could not open. Try again."); }
        finally { opening = false; }
    }
    public void HidePalette()
    {
        IsOpen = false; activationPending = false; generation++; openEpoch++; query?.Cancel();
        acrylic.Update(false, Root.ActualTheme);
        AppWindow.Hide();
        Results.ItemsSource = null;
        TraceSurface("hidden");
    }
    private async Task RetryFocusAsync(long epoch, nint previous, long started)
    {
        try
        {
            for (int attempt = 0; attempt <= 3; attempt++)
            {
                await Task.Delay(20); // let first-show XAML layout finish; bounded to 80 ms
                bool current = IsOpen && !shutdown && openEpoch == epoch;
                nint foreground = GetForegroundWindow();
                var decision = PaletteFocusRules.Decide(current, foreground == hwnd, foreground == previous,
                    SearchBox.FocusState != FocusState.Unfocused, attempt);
                if (decision == PaletteFocusDecision.Cancel) { if (current) HidePalette(); return; }
                if (decision == PaletteFocusDecision.Focused) { activationPending = false; TraceOpen(started, true); return; }
                if (decision == PaletteFocusDecision.Failed) break;
                bool activated = SetForegroundWindow(hwnd) || GetForegroundWindow() == hwnd;
                if (activated && SearchBox.Focus(FocusState.Programmatic))
                { activationPending = false; TraceOpen(started, true); return; }
            }
            HidePalette();
            notify($"Command palette could not take focus. Leave the full-screen or administrator window and try {App.ViewModels.Services.Settings.EffectiveShortcut(FlowDesk.Core.Hotkeys.HotkeyCatalog.PaletteId).Shortcut} again.");
        }
        catch (Exception ex) { Log(ex); if (IsOpen && openEpoch == epoch) { HidePalette(); notify("Command palette could not take focus. Try again."); } }
    }
    private void SetStatus(string text)
    {
        StatusText.Text = text; AutomationProperties.SetName(StatusText, text);
        if (StatusText.IsLoaded) FrameworkElementAutomationPeer.CreatePeerForElement(StatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsOpen && !opening) QueueQuery(); }
    public void SetMode(PaletteMode requested)
    {
        Mode = ClipboardModeRules.Resolve(requested, ClipboardEnabled?.Invoke() == true);
        Results.ItemsSource = null;
        ClipboardModeLabel.Visibility = ClearClipboardButton.Visibility = Mode == PaletteMode.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.PlaceholderText = Mode == PaletteMode.Clipboard ? "Search clipboard history…" : "Search workspaces, apps, actions…";
        KeyboardHint.Text = Mode == PaletteMode.Clipboard ? "Backspace on empty search · Commands" : "↑ ↓ to select · Enter to open · Ctrl+Enter for more";
        SearchBox.Text = "";
        if (IsOpen && !opening) QueueQuery();
    }
    private void ClearClipboard_Click(object sender, RoutedEventArgs e) { ClearClipboard?.Invoke(); SearchBox.Focus(FocusState.Programmatic); }
    public void RefreshResults() { if (IsOpen) { if (Mode == PaletteMode.Clipboard) Results.ItemsSource = null; QueueQuery(true); } }
    private async void QueueQuery(bool preserveSelection = false)
    {
        query?.Cancel(); query?.Dispose(); query = new();
        var token = query.Token; long expected = ++generation; string text = SearchBox.Text;
        try
        {
            BeforeQuery?.Invoke();
            PaletteMode queryMode = Mode;
            await Task.Delay(20, token);
            var matches = queryMode == PaletteMode.Clipboard
                ? ClipboardQuery is null ? [] : await ClipboardQuery(text, token)
                : await Task.Run(() => search.Query(text, token), token);
            if (!IsOpen || generation != expected || token.IsCancellationRequested) return;
            var rows = matches.Select(m => new PaletteResultRow(m)).ToArray();
            string? selectedId = preserveSelection ? (Results.SelectedItem as PaletteResultRow)?.Match.Item.Id : null;
            Results.ItemsSource = rows;
            int selectedIndex = Array.FindIndex(rows, r => r.Match.Item.Id == selectedId);
            Results.SelectedIndex = rows.Length > 0 ? Math.Max(0, selectedIndex) : -1;
            ResizeForResults(rows.Length);
            SetStatus(queryMode == PaletteMode.Clipboard
                ? rows.Length == 0 ? (string.IsNullOrWhiteSpace(text) ? "No history yet · Copy text to get started" : "No matching clipboard items") : rows.Length == 1 ? "1 clipboard item" : $"{rows.Length} clipboard items"
                : rows.Length == 0 ? (string.IsNullOrWhiteSpace(text) ? "Recent · Your recently used commands will appear here" : "No results")
                : (string.IsNullOrWhiteSpace(text) ? "Recent · " : "") + $"{rows.Length} results");
            foreach (var row in rows)
            {
                if (row.Match.Item.Icon.Length == 1) continue;
                if (iconCache.TryGetValue(row.Match.Item.Icon, out var cached)) { row.IconSource = cached; continue; }
                byte[]? bytes = await icons.PaletteIconAsync(row.Match.Item.Icon, token);
                if (bytes is null || !IsOpen || generation != expected || token.IsCancellationRequested) continue;
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
                var image = new BitmapImage(); await image.SetSourceAsync(stream);
                if (iconCache.Count >= 256) iconCache.Clear();
                iconCache[row.Match.Item.Icon] = image;
                if (IsOpen && generation == expected) row.IconSource = image;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log(ex); if (IsOpen && generation == expected) SetStatus("Search is unavailable. Try again."); }
    }
    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        int count = Results.Items.Count;
        int selected = Results.SelectedIndex;
        int next = e.Key switch { VirtualKey.Up => selected - 1, VirtualKey.Down => selected + 1,
            VirtualKey.PageUp => selected - 8, VirtualKey.PageDown => selected + 8, VirtualKey.Home => 0, VirtualKey.End => count - 1, _ => int.MinValue };
        if (next != int.MinValue)
        {
            e.Handled = true;
            if (count > 0) { Results.SelectedIndex = Math.Clamp(next, 0, count - 1); Results.ScrollIntoView(Results.SelectedItem); }
        }
        else Root_KeyDown(sender, e);
    }
    private void ResizeForResults(int count)
    {
        double height = count == 0 ? 148 : Math.Min(540, 116 + Math.Min(8, count) * 52);
        var rect = PalettePlacement.Place(workArea, monitorScale, height);
        AppWindow.MoveAndResize(new RectInt32(rect.X, rect.Y, rect.Width, rect.Height));
        EnsureBorderlessFrame();
        TraceSurface("resized");
    }
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == VirtualKey.Back && ClipboardModeRules.Backspace(Mode, SearchBox.Text) != Mode)
        { e.Handled = true; SetMode(PaletteMode.Commands); return; }
        if (e.Key == VirtualKey.Escape) { e.Handled = true; HidePalette(); }
        if (e.Key == VirtualKey.Enter && Results.SelectedItem is PaletteResultRow row)
        { e.Handled = true; _ = RunAsync(row, (GetKeyState(0x11) & 0x8000) != 0); }
    }
    private void Results_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is PaletteResultRow row) _ = RunAsync(row, false); }
    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var old in e.RemovedItems.OfType<PaletteResultRow>()) old.Hint = "";
        foreach (var row in e.AddedItems.OfType<PaletteResultRow>()) row.Hint = row.Match.Item.Kind == CommandKind.Clipboard ? "Enter · Paste   Ctrl+Enter · Copy" : "Enter · " + row.Match.Item.ActionHint;
    }
    private async Task RunAsync(PaletteResultRow row, bool secondary)
    {
        if (running || !IsOpen) return;
        var action = secondary ? row.Match.Item.Secondary : row.Match.Item.Primary;
        if (action is null) { SetStatus("No secondary action for this item"); return; }
        running = true;
        if (!row.Match.Item.KeepsPaletteOpen) HidePalette();
        try
        {
            await action();
            if (row.Match.Item.RecordUsage)
            {
                usage.Record(row.Match.Item.Id, DateTimeOffset.UtcNow);
                try { await Task.Run(usage.SaveAsync); } catch (Exception ex) { Log(ex); }
            }
        }
        catch (Exception ex) { Log(ex); notify("That command could not run. The app or file may no longer be available."); }
        finally { running = false; }
    }
    private void ResultTitle_Loaded(object sender, RoutedEventArgs e)
        => HighlightTitle(sender);
    private void ResultTitle_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e)
        => HighlightTitle(sender);
    private static void HighlightTitle(object sender)
    {
        if (sender is not TextBlock block || block.DataContext is not PaletteResultRow row) return;
        block.Inlines.Clear(); int at = 0;
        foreach (var range in row.Match.Highlights)
        {
            if (range.Start > at) block.Inlines.Add(new Run { Text = row.Title[at..range.Start] });
            var bold = new Bold(); bold.Inlines.Add(new Run { Text = row.Title.Substring(range.Start, range.Length) }); block.Inlines.Add(bold);
            at = range.Start + range.Length;
        }
        if (at < row.Title.Length) block.Inlines.Add(new Run { Text = row.Title[at..] });
    }
    public void Shutdown()
    {
        if (shutdown) return; shutdown = true; HidePalette();
        acrylic.StateChanged -= UpdateBackdropSurface;
        SystemBackdrop = null; // detach/dispose the native controller before closing the HWND
        icons.Dispose(); Close();
    }
    private void UpdateBackdropSurface()
    {
        SolidFallback.Visibility = acrylic.UseSolidFallback ? Visibility.Visible : Visibility.Collapsed;
        TraceSurface("backdrop-state");
    }
    private static void Log(Exception ex) => new CrashLog().TryWrite(CrashSource.Activation, ex);
    private void EnsureBorderlessFrame()
    {
        uint original = unchecked((uint)(long)GetWindowLongPtrW(hwnd, -16));
        uint desired = PaletteWindowStyle.Apply(original);
        if (original == desired) return;
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtrW(hwnd, -16, (nint)(long)desired) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        // Flush the cached non-client metrics without moving, resizing, reordering or activating.
        if (!SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0020 | 0x0002 | 0x0001 | 0x0004 | 0x0010))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        TraceSurface("frame-applied");
    }
    [Conditional("DEBUG")]
    private static void TraceOpen(long started, bool focused) => Debug.WriteLine($"FlowDesk palette open {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms focused={focused}");
    [Conditional("DEBUG")]
    private void TraceSurface(string stage)
    {
#if DEBUG
        try
        {
            string? path = Environment.GetEnvironmentVariable("FLOWDESK_PALETTE_DIAGNOSTICS_LOG");
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
            int borderHr = DwmGetWindowAttribute(hwnd, 34, out int border, 4);
            int cornerHr = DwmGetWindowAttribute(hwnd, 33, out int corner, 4);
            int cloakHr = DwmGetWindowAttribute(hwnd, 14, out int cloaked, 4);
            GetWindowRect(hwnd, out var window); GetClientRect(hwnd, out var client);
            var origin = new Point(); ClientToScreen(hwnd, ref origin);
            var ui = new Windows.UI.ViewManagement.UISettings();
            var accessibility = new Windows.UI.ViewManagement.AccessibilitySettings();
            GetSystemPowerStatus(out var power);
            string line = $"{DateTimeOffset.UtcNow:O} palette {stage} hwnd=0x{hwnd:X} " +
                $"style=0x{GetWindowLongPtrW(hwnd, -16):X} exstyle=0x{GetWindowLongPtrW(hwnd, -20):X} " +
                $"window={window.Right - window.Left}x{window.Bottom - window.Top} client={client.Right}x{client.Bottom} " +
                $"clientOffset={origin.X - window.Left},{origin.Y - window.Top} " +
                $"border=0x{border:X8}/hr=0x{borderHr:X8} corner={corner}/hr=0x{cornerHr:X8} cloaked={cloaked}/hr=0x{cloakHr:X8} " +
                $"backdrop={SystemBackdrop?.GetType().Name} theme={Root.ActualTheme} open={IsOpen} foreground={GetForegroundWindow() == hwnd} " +
                $"surface={(Root.Background as SolidColorBrush)?.Color} stroke={(Root.BorderBrush as SolidColorBrush)?.Color} " +
                $"strokeThickness={Root.BorderThickness} resultsBackground={(Results.Background as SolidColorBrush)?.Color} " +
                $"acrylicSupported={Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported()} " +
                $"advancedEffects={ui.AdvancedEffectsEnabled} highContrast={accessibility.HighContrast} batterySaver={power.SystemStatusFlag} " +
                $"remoteSession={GetSystemMetrics(0x1000)} " +
                $"fallbackLayer={SolidFallback.Visibility}/{(SolidFallback.Background as SolidColorBrush)?.Color} " +
                $"configuration=[{acrylic.Describe()}]";
            Debug.WriteLine(line);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception) { /* Optional diagnostics must never prevent activation. */ }
#endif
    }
    private static nint CursorMonitor() { GetCursorPos(out var point); return MonitorFromPoint(point, 2); }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
