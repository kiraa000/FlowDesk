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
    public static nint Foreground => GetForegroundWindow();
    public static uint ProcessOf(nint window) { GetWindowThreadProcessId(window, out uint process); return process; }
    public static string ClassOf(nint window) { var text = new StringBuilder(256); GetClassNameW(window, text, text.Capacity); return text.ToString(); }
    public CommandPaletteWindow(CommandSearch search, PaletteUsageStore usage, Action<string> notify)
    {
        this.search = search; this.usage = usage; this.notify = notify;
        InitializeComponent();
        Title = "Command palette — FlowDesk";
        SystemBackdrop = new DesktopAcrylicBackdrop(); // system handles transparency-off/high-contrast fallback
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false); p.IsResizable = false; p.IsMaximizable = false; p.IsMinimizable = false; p.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;
        SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | 0x80); // TOOLWINDOW, deliberately NOT NOACTIVATE
        int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4);
        // Borderless WinUI still inherits DWM's OS-theme active border (white when the app is
        // dark but Windows is light). Let the theme-aware XAML surface stroke own the outline.
        int noNativeBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE, Windows 11+
        DwmSetWindowAttribute(hwnd, 34, ref noNativeBorder, 4); // DWMWA_BORDER_COLOR
        Root.ActualThemeChanged += (_, _) => TraceSurface();
        AppWindow.Resize(new SizeInt32(640, 540));
        AppWindow.Closing += (_, e) => { if (!shutdown) { e.Cancel = true; HidePalette(); } };
        Activated += (_, e) => { if (!opening && !activationPending && IsOpen && e.WindowActivationState == WindowActivationState.Deactivated) HidePalette(); };
        SearchBox.Loaded += (_, _) =>
        {
            if (IsOpen && GetForegroundWindow() == hwnd && SearchBox.Focus(FocusState.Programmatic)) TraceOpen(openStarted, true);
        };
        Results.ContainerContentChanging += (_, e) =>
        {
            if (!e.InRecycleQueue && e.Item is PaletteResultRow row) AutomationProperties.SetName(e.ItemContainer, row.AutomationName);
        };
        AppWindow.Hide();
    }
    public void Open(nint previous, string theme, long started)
    {
        if (shutdown || running) return;
        opening = true;
        activationPending = true;
        openStarted = started;
        long epoch = ++openEpoch;
        try
        {
            Root.RequestedTheme = theme switch { "Dark" => ElementTheme.Dark, "Light" => ElementTheme.Light, _ => ElementTheme.Default };
            TraceSurface();
            nint monitor = previous == 0 ? CursorMonitor() : MonitorFromWindow(previous, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(monitor, ref info)) throw new InvalidOperationException("Display unavailable.");
            double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) >= 0 ? dpi / 96.0 : 1;
            workArea = new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
            monitorScale = scale;
            ResizeForResults(0);
            IsOpen = true; SearchBox.Text = ""; Results.ItemsSource = null; SetStatus("Recent");
            AppWindow.Show(); Activate();
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
        IsOpen = false; activationPending = false; generation++; openEpoch++; query?.Cancel(); AppWindow.Hide();
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
            notify("Command palette could not take focus. Leave the full-screen or administrator window and try Ctrl+Alt+Space again.");
        }
        catch (Exception ex) { Log(ex); if (IsOpen && openEpoch == epoch) { HidePalette(); notify("Command palette could not take focus. Try again."); } }
    }
    private void SetStatus(string text)
    {
        StatusText.Text = text; AutomationProperties.SetName(StatusText, text);
        if (StatusText.IsLoaded) FrameworkElementAutomationPeer.CreatePeerForElement(StatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsOpen && !opening) QueueQuery(); }
    public void RefreshResults() { if (IsOpen) QueueQuery(true); }
    private async void QueueQuery(bool preserveSelection = false)
    {
        query?.Cancel(); query?.Dispose(); query = new();
        var token = query.Token; long expected = ++generation; string text = SearchBox.Text;
        try
        {
            BeforeQuery?.Invoke();
            await Task.Delay(20, token);
            var matches = await Task.Run(() => search.Query(text, token), token);
            if (!IsOpen || generation != expected || token.IsCancellationRequested) return;
            var rows = matches.Select(m => new PaletteResultRow(m)).ToArray();
            string? selectedId = preserveSelection ? (Results.SelectedItem as PaletteResultRow)?.Match.Item.Id : null;
            Results.ItemsSource = rows;
            int selectedIndex = Array.FindIndex(rows, r => r.Match.Item.Id == selectedId);
            Results.SelectedIndex = rows.Length > 0 ? Math.Max(0, selectedIndex) : -1;
            ResizeForResults(rows.Length);
            SetStatus(rows.Length == 0 ? (string.IsNullOrWhiteSpace(text) ? "Recent · Your recently used commands will appear here" : "No results")
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
    }
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == VirtualKey.Escape) { e.Handled = true; HidePalette(); }
        if (e.Key == VirtualKey.Enter && Results.SelectedItem is PaletteResultRow row)
        { e.Handled = true; _ = RunAsync(row, (GetKeyState(0x11) & 0x8000) != 0); }
    }
    private void Results_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is PaletteResultRow row) _ = RunAsync(row, false); }
    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var old in e.RemovedItems.OfType<PaletteResultRow>()) old.Hint = "";
        foreach (var row in e.AddedItems.OfType<PaletteResultRow>()) row.Hint = "Enter · " + row.Match.Item.ActionHint;
    }
    private async Task RunAsync(PaletteResultRow row, bool secondary)
    {
        if (running || !IsOpen) return;
        var action = secondary ? row.Match.Item.Secondary : row.Match.Item.Primary;
        if (action is null) { SetStatus("No secondary action for this item"); return; }
        running = true; HidePalette();
        try
        {
            await action(); usage.Record(row.Match.Item.Id, DateTimeOffset.UtcNow);
            try { await Task.Run(usage.SaveAsync); } catch (Exception ex) { Log(ex); }
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
    public void Shutdown() { if (shutdown) return; shutdown = true; HidePalette(); icons.Dispose(); Close(); }
    private static void Log(Exception ex) => new CrashLog().TryWrite(CrashSource.Activation, ex);
    [Conditional("DEBUG")]
    private static void TraceOpen(long started, bool focused) => Debug.WriteLine($"FlowDesk palette open {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms focused={focused}");
    [Conditional("DEBUG")]
    private void TraceSurface() => Debug.WriteLine($"FlowDesk palette backdrop={SystemBackdrop?.GetType().Name} theme={Root.ActualTheme} surfaceAlpha={(Root.Background as SolidColorBrush)?.Color.A}");
    private static nint CursorMonitor() { GetCursorPos(out var point); return MonitorFromPoint(point, 2); }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
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
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
