using System.Runtime.InteropServices;
using FlowDesk.App.Services;
using FlowDesk.Core.QuickLook;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Streams;

namespace FlowDesk.App.Views;

internal sealed partial class QuickLookWindow : Window
{
    private readonly nint hwnd;
    private readonly SubclassProc subclass;
    private readonly PreviewGeneration generations = new();
    private CancellationTokenSource? load;
    private IQuickLookRenderer? renderer;
    private readonly QuickLookShell shell;
    private bool shuttingDown;
    public event Action? Dismissed;
    public event Action? OpenRequested;
    internal nint NativeHandle => hwnd;
    public QuickLookWindow(QuickLookShell shell)
    {
        this.shell = shell;
        InitializeComponent();
        Title = "Quick Look — FlowDesk";
        SystemBackdrop = new MicaBackdrop();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        }
        AppWindow.IsShownInSwitchers = false;
        SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | 0x08000000 | 0x80); // NOACTIVATE | TOOLWINDOW
        if ((GetWindowLongPtrW(hwnd, -20) & 0x08000000) == 0)
        { Close(); throw new InvalidOperationException("Preview could not be shown safely without activation."); }
        int round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        subclass = Process;
        if (!SetWindowSubclass(hwnd, subclass, 9012, 0))
        { Close(); throw new InvalidOperationException("Preview focus protection could not be installed."); }
        AppWindow.Closing += (_, e) => { if (!shuttingDown) { e.Cancel = true; Dismissed?.Invoke(); } };
    }
    private nint Process(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x21) return 3; // WM_MOUSEACTIVATE / MA_NOACTIVATE: controls may receive clicks, not keyboard focus.
        return DefSubclassProc(window, message, wParam, lParam);
    }
    public void ShowFrame(nint owner, string theme)
    {
        Root.RequestedTheme = theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        var area = DisplayArea.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(owner), DisplayAreaFallback.Nearest).WorkArea;
        // WorkArea and AppWindow coordinates are physical pixels, including on mixed-DPI monitors.
        int width = (int)(area.Width * .65), height = (int)(area.Height * .65);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        FileName.Text = "Quick Look"; Details.Text = "Reading Explorer selection…";
        AppWindow.Show(false);
        ShowWindow(hwnd, 4); // SW_SHOWNOACTIVATE, including first show
    }
    public async Task DisplayAsync(QuickLookItem item)
    {
        ClearContent();
        long generation = generations.Next();
        load = new();
        var token = load.Token;
        FileName.Text = item.Name; Details.Text = item.Type;
        DefaultIcon.Visibility = Visibility.Visible;
        FileIcon.Source = null;
        Loading.Visibility = Visibility.Visible; Loading.IsActive = true;
        Status.Text = "Space or Esc to close · Enter to open";
        try
        {
            var created = await QuickLookRenderers.CreateAsync(item, shell, token);
            if (!generations.IsCurrent(generation)) { created.Dispose(); return; }
            renderer = created;
            var content = await created.LoadAsync(item, token);
            token.ThrowIfCancellationRequested();
            if (!generations.IsCurrent(generation)) return;
            Preview.Content = content;
            Loading.IsActive = false; Loading.Visibility = Visibility.Collapsed;
            Status.Text = created.Note.Length == 0 ? "Space or Esc to close · Enter to open" : created.Note;
            Details.Text = item.Type + created.Details;
            if (item.Path is not null && !item.IsFolder)
            {
                string size = await Task.Run(() =>
                {
                    try { return $" · {new FileInfo(item.Path).Length:N0} bytes"; }
                    catch (Exception) { return ""; }
                }, token);
                if (generations.IsCurrent(generation)) Details.Text += size;
            }
            if (!string.IsNullOrWhiteSpace(item.Identity))
            {
                byte[]? bytes = await shell.ThumbnailAsync(item.Path ?? item.Identity, token);
                if (bytes is not null && generations.IsCurrent(generation))
                {
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
                    var icon = new BitmapImage(); await icon.SetSourceAsync(stream);
                    if (generations.IsCurrent(generation)) { FileIcon.Source = icon; DefaultIcon.Visibility = Visibility.Collapsed; }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (generations.IsCurrent(generation))
            {
                try { renderer?.Dispose(); } catch (Exception) { }
                var fallback = new FallbackQuickLookRenderer(shell, "This file could not be previewed. It may be locked, damaged, or need a Windows codec.");
                renderer = fallback;
                try
                {
                    var content = await fallback.LoadAsync(item, token);
                    if (generations.IsCurrent(generation)) Preview.Content = content;
                }
                catch (Exception) { /* metadata-only fallback remains available through header + Open */ }
                if (generations.IsCurrent(generation)) Status.Text = fallback.Note;
            }
        }
        finally
        {
            if (generations.IsCurrent(generation)) { Loading.IsActive = false; Loading.Visibility = Visibility.Collapsed; }
        }
    }
    private void ClearContent()
    {
        generations.Next(); load?.Cancel(); load?.Dispose(); load = null;
        var old = renderer; renderer = null;
        try { old?.Dispose(); } catch (Exception) { /* playback teardown must not break Close or selection follow */ }
        Preview.Content = null; FileIcon.Source = null; DefaultIcon.Visibility = Visibility.Visible;
    }
    public void HidePreview() { ClearContent(); AppWindow.Hide(); }
    public void Shutdown() { HidePreview(); shuttingDown = true; RemoveWindowSubclass(hwnd, subclass, 9012); Close(); }
    private void Open_Click(object sender, RoutedEventArgs args) => OpenRequested?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs args) => Dismissed?.Invoke();
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
