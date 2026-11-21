using System.Runtime.InteropServices;
using System.Diagnostics;
using FlowDesk.App.Services;
using FlowDesk.Core.Hotkeys;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace FlowDesk.App.Views;

/// <summary>A single reusable, non-interactive HWND. All calls are on the UI dispatcher.</summary>
internal sealed partial class GameModeFlyout : Window
{
    private readonly nint hwnd;
    private readonly SubclassProc subclass;
    private CancellationTokenSource? visible;
    private readonly GameModeFlyoutLifetime lifetime = new();
    private bool shutdown;
    public GameModeFlyout()
    {
        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        }
        AppWindow.IsShownInSwitchers = false;
        SetWindowLongPtrW(hwnd, -16, (nint)PaletteWindowStyle.Apply(unchecked((uint)(long)GetWindowLongPtrW(hwnd, -16))));
        // WS_EX_TRANSPARENT alone only orders painting. With WS_EX_LAYERED it also makes
        // mouse input pass to windows on other threads (HTTRANSPARENT alone does not).
        const int safeStyles = 0x08000000 | 0x80 | 0x20 | 0x80000; // NOACTIVATE, TOOLWINDOW, TRANSPARENT, LAYERED
        SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | safeStyles);
        subclass = Process;
        if ((GetWindowLongPtrW(hwnd, -20) & safeStyles) != safeStyles || !SetWindowSubclass(hwnd, subclass, 9029, 0))
        { Close(); throw new InvalidOperationException("Game mode flyout focus protection is unavailable."); }
        if (!SetLayeredWindowAttributes(hwnd, 0, 255, 2))
        { RemoveWindowSubclass(hwnd, subclass, 9029); Close(); throw new InvalidOperationException("Game mode flyout transparency is unavailable."); }
        int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0037); // refresh native frame without activating or changing order
    }
    public async Task ShowStateAsync(bool enabled, string theme)
    {
        if (shutdown) return;
        visible?.Cancel(); visible?.Dispose(); visible = new();
        AppWindow.Hide(); // A failed re-placement must not leave the cancelled previous flyout visible.
        var token = visible.Token;
        Root.RequestedTheme = theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        StateIcon.Glyph = enabled ? "\uE7FC" : "\uE765";
        StateText.Text = enabled ? "Shortcuts off" : "Shortcuts on";
        SecondaryText.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!GetCursorPos(out var point)) throw new InvalidOperationException("Cursor monitor unavailable.");
        nint monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) throw new InvalidOperationException("Display unavailable.");
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) >= 0 ? dpi / 96.0 : 1;
        var ui = new UISettings();
        var b = GameModeFlyoutRules.Place(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top, scale, enabled, ui.TextScaleFactor);
        AppWindow.MoveAndResize(new RectInt32(b.X, b.Y, b.Width, b.Height));
        // Layered HWNDs are not guaranteed DWM rounding. Clip the acrylic as well as the XAML card.
        int diameter = (int)Math.Round(16 * scale);
        nint region = CreateRoundRectRgn(0, 0, b.Width + 1, b.Height + 1, diameter, diameter);
        if (region != 0 && SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region); // Windows owns successful regions
        bool animate = ui.AnimationsEnabled;
        long epoch = lifetime.Restart(Environment.TickCount64, animate ? 120 : 0);
        if (!SetLayeredWindowAttributes(hwnd, 0, animate ? (byte)0 : (byte)255, 2))
            throw new InvalidOperationException("Game mode flyout transparency is unavailable.");
        AppWindow.Show(false);
        if (!SetWindowPos(hwnd, -1, 0, 0, 0, 0, 0x0013)) // TOPMOST, NOSIZE | NOMOVE | NOACTIVATE
        { AppWindow.Hide(); throw new InvalidOperationException("Flyout could not be raised safely."); }
        try
        {
            var peer = FrameworkElementAutomationPeer.FromElement(Root) ?? new FrameworkElementAutomationPeer(Root);
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent,
                StateText.Text + (enabled ? ". Game mode. Command palette still works." : ". Game mode off."), "FlowDesk.GameMode");
        }
        catch (Exception) { /* A disconnected accessibility client must not leave a permanent overlay. */ }
        try
        {
            if (animate) await FadeAsync(0, 255, 120, token);
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, lifetime.Deadline - Environment.TickCount64)), token);
            if (animate) await FadeAsync(255, 0, 200, token);
            token.ThrowIfCancellationRequested();
            if (!lifetime.IsCurrent(epoch)) return;
            AppWindow.Hide();
            SetWindowPos(hwnd, -2, 0, 0, 0, 0, 0x0013);
        }
        catch (OperationCanceledException) { }
    }
    private async Task FadeAsync(byte from, byte to, int milliseconds, CancellationToken token)
    {
        long began = Stopwatch.GetTimestamp();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            double progress = Math.Min(1, Stopwatch.GetElapsedTime(began).TotalMilliseconds / milliseconds);
            SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(from + (to - from) * progress), 2);
            if (progress >= 1) return;
            await Task.Delay(16, token);
        }
    }
    public void Shutdown()
    {
        if (shutdown) return;
        shutdown = true;
        visible?.Cancel(); visible?.Dispose(); visible = null;
        AppWindow.Hide(); RemoveWindowSubclass(hwnd, subclass, 9029); Close();
    }
    private nint Process(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data) =>
        message == 0x21 ? 3 : message == 0x84 ? -1 : DefSubclassProc(window, message, wParam, lParam); // MA_NOACTIVATE / HTTRANSPARENT
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}
