using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using FlowDesk.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace FlowDesk.App;

// Window lifetime, navigation and theme plumbing only; workspace behaviour lives in view models.
public sealed partial class MainWindow : Window
{
    private readonly AppServices services = new();
    private bool loaded, closing, allowClose, navigating;
    private readonly FocusNotifications notifications;
    private readonly DispatcherTimer focusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer autosaveTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool sampling;
    private bool trayReady;
#if DEBUG
    private Task002Diagnostics? diagnostics;
#endif

    public MainWindow()
    {
        InitializeComponent();
        App.ViewModels = new(services, new UserInteraction(this, Root));
        notifications = new(a => DispatcherQueue.TryEnqueue(() => a()),
            message => App.ViewModels.ShowStatus(message, StatusKind.Warning), async window =>
            {
                if (await Task.Run(() => services.Focus.AllowTemporarily(window)))
                    App.ViewModels.ShowStatus("App allowed for 5 minutes. Open it again to continue.");
                else App.ViewModels.ShowStatus("That allowance is no longer available.");
            }, BringForward);
        services.Hotkeys.Notify = message => DispatcherQueue.TryEnqueue(() => notifications.ShowMessage(message));
        services.Focus.AppBlocked += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            notifications.Blocked(e);
            App.ViewModels.ShowStatus(e.Action == FlowDesk.Core.Focus.BlockAction.Warned
                ? $"{e.AppName} is on your blocked list" : $"{e.AppName} was closed for this focus session.");
        });
        services.Focus.SessionEnded += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            notifications.Complete(); App.ViewModels.ShowStatus("Focus session complete", StatusKind.Success);
            await UpdateFocusAsync();
        });
        App.ViewModels.Focus.SessionChanged += notifications.ClearSession;
        TrayOpen.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(BringForward);
        TrayPause.Command = App.ViewModels.Focus.PauseCommand;
        TrayExit.Command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ExitFromTrayAsync);
        focusTimer.Tick += async (_, _) => await UpdateFocusAsync();
        autosaveTimer.Tick += (_, _) => App.ViewModels.RefreshAutoSaveAge();
        Closed += (_, _) => { focusTimer.Stop(); autosaveTimer.Stop(); notifications.Dispose(); TrayIcon.Dispose(); services.Hotkeys.Dispose(); };
        Root.DataContext = App.ViewModels;
        App.ViewModels.NavigationRequested += Navigate;
        App.ViewModels.ThemeChanged += ApplyTheme;
        InitializeTour();
        Root.ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateCaptionColors);
        App.ViewModels.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(AppViewModels.IsBusy)) return;
            PageFrame.IsEnabled = !App.ViewModels.IsBusy;
            foreach (NavigationViewItem item in Navigation.MenuItems) item.IsEnabled = !App.ViewModels.IsBusy;
            if (Navigation.SettingsItem is NavigationViewItem settings) settings.IsEnabled = !App.ViewModels.IsBusy;
        };
        Title = "FlowDesk"; SystemBackdrop = new MicaBackdrop(); ExtendsContentIntoTitleBar = true;
        TitleBar.Margin = new Thickness(16, 0, AppWindow.TitleBar.RightInset + 16, 0); SetTitleBar(TitleBar);
        AppWindow.Resize(new SizeInt32(1100, 720));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720; presenter.PreferredMinimumHeight = 520;
        }
        Navigation.IsEnabled = false; AppWindow.Closing += OnClosing;
    }
    public void BringForward()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) p.Restore();
        Activate();
    }
    public async Task InitializeAsync()
    {
        try
        {
            await App.ViewModels.LoadAsync(); loaded = true;
            notifications.Initialize();
            if (services.Settings.WindowShortcutsEnabled)
            {
                // Keep the Settings toggle disabled while the startup start is still pending.
                App.ViewModels.Settings.WindowShortcutsBusy = true;
                try { await services.Hotkeys.StartAsync(); }
                catch (Exception ex)
                {
                    App.ViewModels.ShowStatus("Window shortcuts could not start. " + ex.Message, StatusKind.Warning);
                }
                finally
                {
                    App.ViewModels.Settings.WindowShortcutsBusy = false;
                    App.ViewModels.Settings.RefreshWindowShortcutsState();
                }
            }
#if DEBUG
            if (Task002Diagnostics.Enabled) diagnostics = new(services, notifications, () => App.ViewModels.Focus.RefreshAsync());
#endif
            try { TrayIcon.ForceCreate(enablesEfficiencyMode: false); trayReady = true; }
            catch (Exception) { App.ViewModels.ShowStatus("The tray icon could not be created. Keep this window open during focus.", StatusKind.Warning); }
            focusTimer.Start();
            App.ViewModels.AutoSave.Start();
            // Resuming after Start keeps the activation generation valid for its first published status.
            await App.ViewModels.ResumeActiveWorkspaceAsync();
            autosaveTimer.Start();
            var s = services.Settings; var area = DisplayArea.Primary.WorkArea;
            int width = Math.Clamp(s.Width, 720, Math.Max(720, area.Width));
            int height = Math.Clamp(s.Height, 520, Math.Max(520, area.Height));
            int x = Math.Clamp(s.X ?? area.X + 80, area.X, Math.Max(area.X, area.X + area.Width - width));
            int y = Math.Clamp(s.Y ?? area.Y + 80, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
            Navigation.IsEnabled = true; Navigate(AppPage.Workspaces);
            if (!services.Data.TourCompleted) App.ViewModels.RequestTour();
        }
        catch (Exception ex)
        {
            App.ViewModels.ShowStatus("Could not load workspaces. Check the data folder and restart. " + ex.Message,
                StatusKind.Error);
        }
    }
    private bool browserHelpOpen;
    private async void BrowserHelp_Click(object sender, RoutedEventArgs e)
    {
        if (browserHelpOpen) return;
        browserHelpOpen = true;
        try
        {
            BrowserHelpDialog.XamlRoot = Root.XamlRoot;
            BrowserHelpDialog.RequestedTheme = Root.RequestedTheme;
            await BrowserHelpDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // A second dialog request while another is open is benign; report it instead of crashing.
            App.ViewModels.ShowStatus("The browser help could not be opened. " + ex.Message, StatusKind.Warning);
        }
        finally { browserHelpOpen = false; }
    }
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (navigating || !loaded || App.ViewModels.IsBusy) return;
        var page = args.IsSettingsSelected ? AppPage.Settings
            : Enum.TryParse<AppPage>((args.SelectedItem as NavigationViewItem)?.Tag?.ToString(), out var selected)
                ? selected : AppPage.Workspaces;
        Navigate(page);
    }
    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (!loaded || App.ViewModels.IsBusy) return;
        if (args.IsSettingsInvoked) Navigate(AppPage.Settings);
        else if (Enum.TryParse<AppPage>(args.InvokedItemContainer?.Tag?.ToString(), out var page)) Navigate(page);
    }
    private void Navigate(AppPage page)
    {
        App.ViewModels.Details.Workspace?.CommitName();
        navigating = true;
        try
        {
            var type = page switch
            {
                AppPage.Details => typeof(WorkspaceDetailsPage), AppPage.Capture => typeof(SaveOpenWindowsPage),
                AppPage.Settings => typeof(SettingsPage), AppPage.Layouts => typeof(LayoutsPage),
                AppPage.Focus => typeof(FocusPage), _ => typeof(WorkspacesPage)
            };
            if (PageFrame.CurrentSourcePageType != type) PageFrame.Navigate(type);
            // Navigation is command-driven, not a browser history of stale editing pages.
            PageFrame.BackStack.Clear();
            Navigation.SelectedItem = page == AppPage.Settings ? Navigation.SettingsItem
                : Navigation.MenuItems.Cast<NavigationViewItem>()
                    .First(i => i.Tag.ToString() == (page is AppPage.Details or AppPage.Capture ? "Workspaces" : page.ToString()));
        }
        finally { navigating = false; }
    }
    private void ApplyTheme() => Root.RequestedTheme = services.Settings.Theme switch
    {
        "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default
    };
    private void UpdateCaptionColors()
    {
        if (TitleText.Foreground is not SolidColorBrush text) return;
        AppWindow.TitleBar.ButtonForegroundColor = text.Color;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = text.Color;
        AppWindow.TitleBar.ButtonHoverForegroundColor = text.Color;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        StopTour();
        if (allowClose) return;
        args.Cancel = true;
        if (closing) return;
        // Fresh worker-side state, not the last timer sample, determines whether closing hides.
        if (loaded)
        {
            closing = true;
            bool focusing = await Task.Run(() => services.Focus.State is not null);
            closing = false;
            if (focusing)
            {
                if (trayReady) AppWindow.Hide();
                else App.ViewModels.ShowStatus("Keep this window open until the focus session ends: the tray is unavailable.", StatusKind.Warning);
                return;
            }
        }
        if (App.ViewModels.IsBusy) { App.ViewModels.ShowStatus("Wait for the current operation to finish before closing."); return; }
        if (!loaded) { allowClose = true; Close(); return; }
        closing = true;
        // Do not let a new focus session or autosave tick start while the final persistence await is in flight.
        PageFrame.IsEnabled = false; Navigation.IsEnabled = false; focusTimer.Stop(); autosaveTimer.Stop();
        await App.ViewModels.AutoSave.StopAsync();
        var s = services.Settings;
        s.Width = AppWindow.Size.Width; s.Height = AppWindow.Size.Height;
        s.X = AppWindow.Position.X; s.Y = AppWindow.Position.Y;
        await services.FlushAsync();
        if (!services.LastSaveSucceeded)
        {
            closing = false; PageFrame.IsEnabled = true; Navigation.IsEnabled = true;
            focusTimer.Start(); autosaveTimer.Start(); App.ViewModels.AutoSave.Start(); return;
        }
        await Task.Run(() => services.Focus.Dispose());
        await services.Hotkeys.StopAsync();
        allowClose = true; Close();
    }
    private async Task UpdateFocusAsync()
    {
        if (sampling) return;
        sampling = true;
        try
        {
#if DEBUG
            if (diagnostics is not null) await diagnostics.PollAsync();
#endif
            await App.ViewModels.Focus.RefreshAsync();
            var vm = App.ViewModels.Focus;
            TrayIcon.ToolTipText = vm.IsRunning ? $"FlowDesk · {vm.RemainingText} remaining" : "FlowDesk";
            TrayPause.Text = vm.PauseLabel; TrayPause.Visibility = vm.CanPause ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { sampling = false; }
    }
    private async Task ExitFromTrayAsync()
    {
        BringForward();
        if (App.ViewModels.IsBusy) { App.ViewModels.ShowStatus("Wait for the current operation to finish before exiting."); return; }
        if (!await App.ViewModels.Focus.EndSessionAsync()) return;
        Close();
    }
}
