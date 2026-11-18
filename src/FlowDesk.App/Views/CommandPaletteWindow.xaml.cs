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
    public string AutomationName => (ConfirmationPrompt ?? Title) + ", " + Subtitle;
    public string Glyph => match.Item.Icon.Length == 1 ? match.Item.Icon : match.Item.Kind == CommandKind.App ? "\uE71D" : "\uE8A5";
    [ObservableProperty] public partial ImageSource? IconSource { get; set; }
    public Visibility IconFallbackVisibility => IconSource is null ? Visibility.Visible : Visibility.Collapsed;
    partial void OnIconSourceChanged(ImageSource? value) => OnPropertyChanged(nameof(IconFallbackVisibility));
    [ObservableProperty] public partial string Hint { get; set; } = "";
    [ObservableProperty] public partial string? ConfirmationPrompt { get; set; }
    public Visibility NormalTitleVisibility => ConfirmationPrompt is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ConfirmationTitleVisibility => ConfirmationPrompt is null ? Visibility.Collapsed : Visibility.Visible;
    partial void OnConfirmationPromptChanged(string? value)
    {
        OnPropertyChanged(nameof(NormalTitleVisibility));
        OnPropertyChanged(nameof(ConfirmationTitleVisibility));
        OnPropertyChanged(nameof(AutomationName));
    }
}

internal sealed partial class CommandPaletteWindow : Window
{
    private readonly PaletteStreamingSearch search;
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
    private bool foregroundAcquired;
    private bool changingMode, resultsCurrent;
    private PaletteActionPanel<PaletteResultRow>? panel;
    private bool refreshAfterPanel;
    private readonly PaletteActionExecutor executor = new(() => Stopwatch.GetElapsedTime(ProcessStarted));
    private static readonly long ProcessStarted = Stopwatch.GetTimestamp();
    private bool refreshAfterConfirmation;
    private PaletteResultRow? confirmationRow;
    private long confirmationVersion;
    public bool IsOpen { get; private set; }
    public Action? BeforeQuery { get; set; }
    public Func<bool>? ClosePreview { get; set; }
    public nint Handle => hwnd;
    public PaletteMode Mode { get; private set; }
    public Func<bool>? ClipboardEnabled { get; set; }
    public Func<PaletteMode, bool> ModeEnabled { get; set; } = _ => true;
    public Func<string, CancellationToken, Task<IReadOnlyList<CommandMatch>>>? ClipboardQuery { get; set; }
    public Action? ClearClipboard { get; set; }
    public static nint Foreground => GetForegroundWindow();
    public static uint ProcessOf(nint window) { GetWindowThreadProcessId(window, out uint process); return process; }
    public static string ClassOf(nint window) { var text = new StringBuilder(256); GetClassNameW(window, text, text.Capacity); return text.ToString(); }
    public CommandPaletteWindow(PaletteStreamingSearch search, PaletteUsageStore usage, Action<string> notify)
    {
        this.search = search; this.usage = usage; this.notify = notify;
        InitializeComponent();
        answer.Changed += QueueAnswerRender;
        Root.AddHandler(UIElement.CharacterReceivedEvent,
            new Windows.Foundation.TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>(Answer_CharacterReceived), true);
        answerRenderTimer.Tick += (_, _) => { answerRenderTimer.Stop(); if (!shutdown) RenderAnswer(); };
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
            if (IsOpen && !activationPending && e.WindowActivationState != WindowActivationState.Deactivated && GetForegroundWindow() == hwnd)
                foregroundAcquired = true;
#if DEBUG
            // Configuration updates can run after Window.Activated; capture the settled value too.
            DispatcherQueue.TryEnqueue(() => TraceSurface("activation-settled"));
#endif
            if (e.WindowActivationState == WindowActivationState.Deactivated &&
                PaletteFocusRules.ShouldHideOnDeactivation(IsOpen, opening, activationPending, foregroundAcquired)) HidePalette();
        };
        Root.Loaded += (_, _) => TraceSurface("root-loaded");
        SearchBox.Loaded += (_, _) =>
        {
            if (IsOpen && GetForegroundWindow() == hwnd && SearchBox.Focus(FocusState.Programmatic)) TraceOpen(openStarted, true);
        };
        Root.PreviewKeyDown += Root_KeyDown;
        Results.ContainerContentChanging += (_, e) =>
        {
            if (!e.InRecycleQueue && e.Item is PaletteResultRow row) SetAccessibleName(e.ItemContainer, row.AutomationName);
        };
        AppWindow.Hide();
        TraceSurface("constructed-hidden");
    }
    public void Open(nint previous, string theme, long started, PaletteMode mode = PaletteMode.Commands)
    {
        if (shutdown || running) return;
        opening = true;
        activationPending = true;
        foregroundAcquired = false;
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
            SetForegroundWindow(hwnd);
            bool foreground = GetForegroundWindow() == hwnd; // the native return can mean an asynchronous request was accepted
            bool focused = SearchBox.Focus(FocusState.Programmatic);
            if (!foreground || !focused) _ = RetryFocusAsync(epoch, previous, started);
            else { activationPending = false; foregroundAcquired = true; TraceOpen(started, true); }
            QueueQuery();
        }
        catch (Exception ex) { Log(ex); HidePalette(); notify("Command palette could not open. Try again."); }
        finally { opening = false; }
    }
    public void HidePalette()
    {
        ClosePreview?.Invoke();
        IsOpen = false; activationPending = false; foregroundAcquired = false; generation++; openEpoch++; query?.Cancel();
        answer.Close();
        CancelConfirmation(); CloseActions(restore: false);
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
                if (current && attempt == 0 && foreground != hwnd)
                { RequestForegroundWithInputAttachment(); foreground = GetForegroundWindow(); }
                var decision = PaletteFocusRules.Decide(current, foreground == hwnd, foreground == previous,
                    SearchBox.FocusState != FocusState.Unfocused, attempt);
                if (decision == PaletteFocusDecision.Cancel)
                { if (current) FocusUnavailable(); return; }
                if (decision == PaletteFocusDecision.Focused) { activationPending = false; foregroundAcquired = true; TraceOpen(started, true); return; }
                if (decision == PaletteFocusDecision.Failed) break;
                if (GetForegroundWindow() == hwnd && SearchBox.Focus(FocusState.Programmatic))
                { activationPending = false; foregroundAcquired = true; TraceOpen(started, true); return; }
            }
            FocusUnavailable();
        }
        catch (Exception ex) { Log(ex); if (IsOpen && openEpoch == epoch) FocusUnavailable(); }
    }
    private void FocusUnavailable()
    {
        activationPending = false;
        TraceSurface("focus-unavailable-kept-visible");
        SetStatus("Palette is open · Click the search box to focus");
    }
    private void RequestForegroundWithInputAttachment()
    {
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint ownThread = GetCurrentThreadId();
        bool attached = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != ownThread)
                attached = AttachThreadInput(ownThread, foregroundThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally { if (attached) AttachThreadInput(ownThread, foregroundThread, false); }
        TraceSurface("foreground-attachment-retry");
    }
    private void SetStatus(string text)
    {
        StatusText.Text = text; AutomationProperties.SetName(StatusText, text);
        if (StatusText.IsLoaded) FrameworkElementAutomationPeer.CreatePeerForElement(StatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsOpen || opening || changingMode) return;
        ClosePreview?.Invoke();
        CancelConfirmation();
        if (panel is not null) CloseActions();
        string text = SearchBox.Text;
        if (Mode == PaletteMode.Commands && PalettePrefixes.Match(text) is { } prefix && ModeEnabled(prefix.Mode))
            SetMode(prefix.Mode, text[prefix.Prefix.Length..].TrimStart());
        else if (Mode == PaletteMode.Commands && text.Equals("help", StringComparison.OrdinalIgnoreCase)) SetMode(PaletteMode.Help);
        else QueueQuery();
    }
    public void SetMode(PaletteMode requested)
        => SetMode(requested, "");
    private void SetMode(PaletteMode requested, string text)
    {
        ClosePreview?.Invoke();
        CancelConfirmation(); CloseActions(restore: false);
        Mode = (requested == PaletteMode.Clipboard && ClipboardEnabled?.Invoke() != true) || !ModeEnabled(requested)
            ? PaletteMode.Commands : requested;
        var prefix = PalettePrefixes.All.FirstOrDefault(p => p.Mode == Mode);
        Results.ItemsSource = null;
        ClipboardModeLabel.Visibility = Mode == PaletteMode.Commands ? Visibility.Collapsed : Visibility.Visible;
        ClearClipboardButton.Visibility = Mode == PaletteMode.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        ClipboardModeLabel.Text = prefix?.Label ?? (Mode == PaletteMode.Help ? "Help" : "Clipboard");
        SearchBox.PlaceholderText = prefix?.Placeholder ?? (Mode switch { PaletteMode.Clipboard => "Search clipboard history…",
            PaletteMode.Help => "Choose a search mode…", _ => "Search workspaces, apps, actions…" });
        changingMode = true;
        try { SearchBox.Text = text; SearchBox.SelectionStart = text.Length; }
        finally { changingMode = false; }
        UpdateHint();
        if (IsOpen && !opening) QueueQuery();
    }
    private void ClearClipboard_Click(object sender, RoutedEventArgs e) { ClearClipboard?.Invoke(); SearchBox.Focus(FocusState.Programmatic); }
    public void RefreshResults()
    {
        if (!IsOpen) return;
        if (answer.Visible) return;
        if (executor.IsPending) { refreshAfterConfirmation = true; return; }
        if (panel is not null) { refreshAfterPanel = true; return; }
        QueueQuery(true);
    }
    private async void QueueQuery(bool preserveSelection = false)
    {
        query?.Cancel(); query?.Dispose(); query = new();
        var token = query.Token; long expected = ++generation; string text = SearchBox.Text;
        if (!preserveSelection) { resultsCurrent = false; UpdateHint(); }
        try
        {
            BeforeQuery?.Invoke();
            PaletteMode queryMode = Mode;
            bool first = true;
            await foreach (var matches in QueryMatchesAsync(text, queryMode, token))
            {
                if (!IsOpen || generation != expected || token.IsCancellationRequested) return;
                // A source finishing must not erase an already visible prompt or change its target.
                if (executor.IsPending) { refreshAfterConfirmation = true; return; }
                var rows = matches.Select(m => new PaletteResultRow(m)).ToArray();
                string? selectedId = !first || preserveSelection ? (Results.SelectedItem as PaletteResultRow)?.Match.Item.Id : null;
                first = false;
                resultsCurrent = true;
                Results.ItemsSource = rows;
                Results.SelectedIndex = PaletteResultMerge.Selection(matches, selectedId);
                UpdateHint();
                ResizeForResults(rows.Length);
                SetStatus(queryMode == PaletteMode.Clipboard
                ? rows.Length == 0 ? (string.IsNullOrWhiteSpace(text) ? "No history yet · Copy text to get started" : "No matching clipboard items") : rows.Length == 1 ? "1 clipboard item" : $"{rows.Length} clipboard items"
                : rows.Length == 0 ? (queryMode == PaletteMode.Commands && string.IsNullOrWhiteSpace(text)
                    ? "Recent · Your recently used commands will appear here" : queryMode switch
                    {
                        PaletteMode.Calculator when text.Length == 0 => "Try 5 km to mi or time in Tokyo",
                        PaletteMode.Web when text.Length == 0 => "Type a search or website address",
                        PaletteMode.Windows when text.Length == 0 => "No open windows found",
                        PaletteMode.Files when text.Length == 0 => "Type a file or folder name",
                        PaletteMode.Help => "Search modes are off in Settings", _ => "No results"
                    }) : (queryMode == PaletteMode.Commands && string.IsNullOrWhiteSpace(text) ? "Recent · " : "") +
                    (rows.Length == 1 ? "1 result" : $"{rows.Length} results"));
                _ = LoadIconsAsync(rows, token, expected);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log(ex); if (IsOpen && generation == expected) SetStatus("Search is unavailable. Try again."); }
    }
    private async IAsyncEnumerable<IReadOnlyList<CommandMatch>> QueryMatchesAsync(string text, PaletteMode mode,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        if (mode == PaletteMode.Clipboard) yield return ClipboardQuery is null ? [] : await ClipboardQuery(text, token);
        else if (mode == PaletteMode.Help) yield return PalettePrefixes.Help(ModeEnabled, requested => { SetMode(requested); return Task.CompletedTask; });
        else await foreach (var batch in search.QueryAsync(text, mode, token)) yield return batch;
    }
    private async Task LoadIconsAsync(IEnumerable<PaletteResultRow> rows, CancellationToken token, long expected)
    {
        try
        {
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
        catch (Exception ex) { Log(ex); }
    }
    private bool MoveSelection(VirtualKey key)
    {
        int count = Results.Items.Count;
        int selected = Results.SelectedIndex;
        int next = key switch { VirtualKey.Up => selected - 1, VirtualKey.Down => selected + 1,
            VirtualKey.PageUp => selected - 8, VirtualKey.PageDown => selected + 8, VirtualKey.Home => 0, VirtualKey.End => count - 1, _ => int.MinValue };
        if (next == int.MinValue) return false;
        ClosePreview?.Invoke();
        if (count > 0) { Results.SelectedIndex = Math.Clamp(next, 0, count - 1); Results.ScrollIntoView(Results.SelectedItem); }
        return true;
    }
    private void ResizeForResults(int count)
    {
        double height = count == 0 ? 152 : Math.Min(564, 144 + Math.Min(8, count) * 52);
        ResizeForHeight(height);
    }
    private PaletteRect? lastPlacement;
    private void ResizeForHeight(double height)
    {
        var rect = PalettePlacement.Place(workArea, monitorScale, height);
        if (rect == lastPlacement) return;
        lastPlacement = rect;
        AppWindow.MoveAndResize(new RectInt32(rect.X, rect.Y, rect.Width, rect.Height));
        EnsureBorderlessFrame();
        TraceSurface("resized");
    }
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || !IsOpen) return;
        if (e.Key == VirtualKey.Enter) TraceSurface($"enter repeat={e.KeyStatus.WasKeyDown} current={resultsCurrent} running={running} panel={panel is not null}");
        bool ctrl = (GetKeyState(0x11) & 0x8000) != 0;
        if (HandleAnswerKey(e, ctrl)) return;
        if (ctrl && e.Key == VirtualKey.Y)
        {
            e.Handled = true;
            if (!e.KeyStatus.WasKeyDown && ClosePreview?.Invoke() != true && panel is null && Results.SelectedItem is PaletteResultRow previewRow)
            {
                int previewIndex = previewRow.Match.Item.Actions.ToList().FindIndex(a => a.Title == "Quick Look");
                if (previewIndex >= 0) _ = RunAsync(previewRow, previewIndex, PaletteInvocation.ModifiedEnter);
            }
            return;
        }
        if (e.Key == VirtualKey.Escape && ClosePreview?.Invoke() == true) { e.Handled = true; return; }
        if (e.Key == VirtualKey.Enter || e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.PageUp or VirtualKey.PageDown) ClosePreview?.Invoke();
        if (e.Key != VirtualKey.Enter) CancelConfirmation();
        if (ctrl && e.Key == VirtualKey.K)
        { e.Handled = true; if (!e.KeyStatus.WasKeyDown) ToggleActions(); return; }
        if (e.Key == VirtualKey.Escape)
        { e.Handled = true; if (panel is not null) CloseActions(); else HidePalette(); return; }
        if (panel is null && e.Key == VirtualKey.Back && PalettePrefixes.Backspace(Mode, SearchBox.Text) != Mode)
        { e.Handled = true; SetMode(PaletteMode.Commands); return; }
        var focus = FocusManager.GetFocusedElement(Root.XamlRoot);
        if (focus is Button) { CancelConfirmation(); return; }
        if (MoveSelection(e.Key)) { e.Handled = true; return; }
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (e.KeyStatus.WasKeyDown) return;
            bool plainEnter = !ctrl && (GetKeyState(0x10) & 0x8000) == 0 && (GetKeyState(0x12) & 0x8000) == 0;
            var input = plainEnter ? PaletteInvocation.Enter : PaletteInvocation.ModifiedEnter;
            if (panel is { } p) _ = RunAsync(p.Target, Results.SelectedIndex, input);
            else if (Results.SelectedItem is PaletteResultRow row) _ = RunAsync(row, ctrl ? 1 : 0, input);
        }
        else if (panel is not null && e.Key != VirtualKey.Tab) e.Handled = true;
    }
    private void Results_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PaletteResultRow row) return;
        if (!ReferenceEquals(Results.SelectedItem, row)) Results.SelectedItem = row;
        SearchBox.Focus(FocusState.Programmatic);
        if (panel is { } p) _ = RunAsync(p.Target, Results.Items.IndexOf(row), PaletteInvocation.Pointer);
        else _ = RunAsync(row, 0, PaletteInvocation.Pointer);
    }
    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CancelConfirmation(); UpdateHint();
    }
    private void UpdateHint()
    {
        if (answer.Visible && panel is null)
        {
            if (StatusText.Text != "AI answer") SetStatus("AI answer");
            KeyboardHint.Text = answer.Streaming ? "Esc · Cancel" : answer.Answer.Length > 0 ? "Enter · Copy answer   Esc · Results" : "Esc · Results";
            ActionsButton.IsEnabled = true; HelpButton.Visibility = Visibility.Collapsed; return;
        }
        var row = Results.SelectedItem as PaletteResultRow;
        KeyboardHint.Text = panel is not null ? "Enter · Run   Esc · Back" : resultsCurrent && row is not null
            ? row.Match.Item.KeyboardHint : Mode == PaletteMode.Commands ? "↑ ↓ to select" : "Backspace on empty search · Commands";
        ActionsButton.IsEnabled = panel is not null || (resultsCurrent && row?.Match.Item.Actions.Count > 0);
        HelpButton.Visibility = panel is null && Mode == PaletteMode.Commands && SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Actions_Click(object sender, RoutedEventArgs e) => ToggleActions();
    private void Help_Click(object sender, RoutedEventArgs e) { SetMode(PaletteMode.Help); SearchBox.Focus(FocusState.Programmatic); }
    private void ToggleActions()
    {
        ClosePreview?.Invoke();
        CancelConfirmation();
        if (panel is not null) { CloseActions(); return; }
        var row = answer.Visible ? AnswerActions() : Results.SelectedItem as PaletteResultRow;
        if ((!resultsCurrent && !answer.Visible) || row is null || row.Match.Item.Actions.Count == 0) return;
        panel = new(row, Results.ItemsSource, Results.SelectedIndex, StatusText.Text);
        query?.Cancel(); generation++;
        Results.ItemsSource = row.Match.Item.Actions.Select((action, index) => new PaletteResultRow(new(new(
            "action-panel:" + index, action.Title, action.Shortcut ?? "", CommandKind.Action, action.Glyph,
            () => Task.CompletedTask, NamedActions: []), 1, []))).ToArray();
        Results.SelectedIndex = 0;
        AutomationProperties.SetName(Results, "Actions for " + row.Title);
        ResizeForResults(Results.Items.Count); SetStatus("Actions · " + row.Title); UpdateHint();
        SearchBox.Focus(FocusState.Programmatic);
        if (answer.Visible) RenderAnswer();
    }
    private void CloseActions(bool restore = true)
    {
        if (panel is not { } previous) return;
        panel = null; CancelConfirmation();
        AutomationProperties.SetName(Results, "Command palette results");
        if (restore)
        {
            Results.ItemsSource = previous.Results;
            Results.SelectedIndex = previous.Selection;
            if (Results.SelectedItem is { } selected) Results.ScrollIntoView(selected);
            if (!answer.Visible) ResizeForResults(Results.Items.Count);
            SetStatus(previous.Status); UpdateHint();
            SearchBox.Focus(FocusState.Programmatic);
            if (answer.Visible) RenderAnswer();
            if (refreshAfterPanel && !answer.Visible) QueueQuery(true);
        }
        refreshAfterPanel = false;
    }
    private void CancelConfirmation()
    {
        if (executor.IsPending) TraceSurface("confirmation-cancel");
        executor.Cancel(); confirmationVersion++;
        if (confirmationRow is { } row)
        {
            row.ConfirmationPrompt = null;
            if (Results.ContainerFromItem(row) is ListViewItem container) SetAccessibleName(container, row.AutomationName);
            confirmationRow = null;
        }
        if (ConfirmationText?.Visibility == Visibility.Visible)
        {
            ConfirmationText.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAccessibilityView(ConfirmationText, AccessibilityView.Raw);
            SetAccessibleName(ConfirmationText, "");
            StatusText.Visibility = KeyboardHint.Visibility = Visibility.Visible;
            AutomationProperties.SetAccessibilityView(KeyboardHint, AccessibilityView.Content);
        }
        if (refreshAfterConfirmation)
        {
            refreshAfterConfirmation = false;
            // Do not synchronously replace the selection inside the key/click event cancelling the prompt.
            DispatcherQueue.TryEnqueue(() => { if (IsOpen) RefreshResults(); });
        }
    }
    private async Task ExpireConfirmationAsync(long version)
    {
        await Task.Delay(PaletteConfirmation.Duration);
        if (version == confirmationVersion) CancelConfirmation();
    }
    private async Task RunAsync(PaletteResultRow row, int index, PaletteInvocation input)
    {
        if (running || !IsOpen || !resultsCurrent) return;
        running = true;
        try
        {
            PaletteAction? executedAction = null;
            var outcome = await executor.InvokeAsync(row.Match.Item, index, input, action =>
                {
                    TraceSurface("confirmation-prompt");
                    ConfirmationText.Text = action.ConfirmationPrompt ?? "Press Enter again to confirm";
                    if (confirmationRow is { } prior) prior.ConfirmationPrompt = null;
                    confirmationRow = Results.SelectedItem as PaletteResultRow;
                    if (confirmationRow is not null) confirmationRow.ConfirmationPrompt = ConfirmationText.Text;
                    if (confirmationRow is not null && Results.ContainerFromItem(confirmationRow) is ListViewItem container)
                        SetAccessibleName(container, confirmationRow.AutomationName);
                    // Explicit content membership and a name-change notification are important for
                    // already-realized/cached UIA peers, not just the next container realization.
                    SetAccessibleName(ConfirmationText, ConfirmationText.Text);
                    StatusText.Visibility = KeyboardHint.Visibility = Visibility.Collapsed;
                    AutomationProperties.SetAccessibilityView(KeyboardHint, AccessibilityView.Raw);
                    AutomationProperties.SetAccessibilityView(ConfirmationText, AccessibilityView.Content);
                    ConfirmationText.Visibility = Visibility.Visible;
                    _ = ExpireConfirmationAsync(++confirmationVersion);
                    FrameworkElementAutomationPeer.CreatePeerForElement(ConfirmationText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }, action =>
                {
                    executedAction = action;
                    CancelConfirmation();
                    if (!action.KeepsPaletteOpen) HidePalette();
                    else if (panel is not null) CloseActions();
                });
            if (outcome == PaletteExecution.Invalid) CancelConfirmation();
            if (outcome == PaletteExecution.Executed && row.Match.Item.RecordUsage && executedAction?.NeedsConfirm == false)
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
    private static void SetAccessibleName(FrameworkElement element, string name)
    {
        string oldName = AutomationProperties.GetName(element);
        AutomationProperties.SetName(element, name);
        if (oldName != name)
            FrameworkElementAutomationPeer.CreatePeerForElement(element)?.RaisePropertyChangedEvent(
                AutomationElementIdentifiers.NameProperty, oldName, name);
    }
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
        answerRenderTimer.Stop(); answer.Changed -= QueueAnswerRender;
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
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
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
