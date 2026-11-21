using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using FlowDesk.App.Views;
using FlowDesk.Core.Hotkeys;
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
    private bool loaded, closing, allowClose, navigating, initializing = true;
    private readonly FocusNotifications notifications;
    private readonly DispatcherTimer focusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer autosaveTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool sampling;
    private bool trayReady;
    private bool exitRequested, endingSession;
    private readonly SessionEndHook sessionEnd;
    private CommandPaletteService? palette;
    private GameModeFlyout? gameFlyout;
    private readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool gameSampling;
    private RectInt32 normalBounds;
    private readonly AdvancedDialogs advancedDialogs;
#if DEBUG
    private Task002Diagnostics? diagnostics;
#endif

    public MainWindow()
    {
        InitializeComponent();
        services.QuickLook = new(DispatcherQueue, services);
        sessionEnd = new(WinRT.Interop.WindowNative.GetWindowHandle(this), OnSessionEnd);
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "FlowDesk.ico");
        AppWindow.SetIcon(iconPath);
        // The tray must get a real Icon: a BitmapImage IconSource (ms-appx) crashes startup in this unpackaged
        // app (stowed exception in CoreMessagingXP). The XAML GeneratedIconSource stays as the fallback.
        try { if (File.Exists(iconPath)) TrayIcon.UpdateIcon(new System.Drawing.Icon(iconPath, 32, 32)); }
        catch (Exception) { }
        App.ViewModels = new(services, new UserInteraction(this, Root));
        InitializeTransfer();
        advancedDialogs = new(Root);
        advancedDialogs.TakeReturnFocus = () => (PageFrame.Content as AdvancedPage)?.TakeDialogFocusReturn();
        App.ViewModels.Advanced.ConfirmEnable = advancedDialogs.EnableAsync;
        App.ViewModels.Advanced.ConfirmInstall = advancedDialogs.InstallAsync;
        App.ViewModels.Advanced.ConfirmWinUtil = advancedDialogs.WinUtilAsync;
        services.HotCorners = new(services.Hotkeys, DispatcherQueue,
            stillAllowed => closing || exitRequested || endingSession || initializing ? Task.FromResult(false) : App.ViewModels.Focus.TryStartFromHotCornerAsync(stillAllowed));
        notifications = new(a => DispatcherQueue.TryEnqueue(() => a()),
            message => App.ViewModels.ShowStatus(message, StatusKind.Warning), async window =>
            {
                if (await Task.Run(() => services.Focus.AllowTemporarily(window)))
                    App.ViewModels.ShowStatus("App allowed for 5 minutes. Open it again to continue.");
                else App.ViewModels.ShowStatus("That allowance is no longer available.");
            }, BringForward);
        services.Hotkeys.Notify = message => DispatcherQueue.TryEnqueue(() => { if (!services.Hotkeys.GameMode) notifications.ShowMessage(message); });
        services.Focus.AppBlocked += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!services.Hotkeys.GameMode) notifications.Blocked(e);
            App.ViewModels.ShowStatus(e.Action == FlowDesk.Core.Focus.BlockAction.Warned
                ? $"{e.AppName} is on your blocked list" : $"{e.AppName} was closed for this focus session.");
        });
        services.Focus.SessionEnded += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            if (!services.Hotkeys.GameMode) notifications.Complete(); else notifications.ClearSession();
            App.ViewModels.ShowStatus("Focus session complete", StatusKind.Success);
            await UpdateFocusAsync();
        });
        App.ViewModels.Focus.SessionChanged += notifications.ClearSession;
        TrayOpen.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(BringForward);
        TrayIcon.LeftClickCommand = TrayOpen.Command;
        TrayPause.Command = App.ViewModels.Focus.PauseCommand;
        TrayExit.Command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ExitFromTrayAsync);
        TrayPalette.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => palette?.Open());
        TrayGameMode.Command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ToggleGameModeAsync);
        services.Hotkeys.ToggleGameModeRequested = () => DispatcherQueue.TryEnqueue(async () => await ToggleGameModeAsync());
        services.Hotkeys.GameModeChanged += enabled => DispatcherQueue.TryEnqueue(async () =>
        {
            if (allowClose || endingSession) return;
            TrayGameMode.IsChecked = enabled;
            TrayIcon.ToolTipText = enabled ? "FlowDesk — game mode (shortcuts off)" : "FlowDesk";
            App.ViewModels.Hotkeys.RefreshStatus();
            try { gameFlyout ??= new(); await gameFlyout.ShowStateAsync(enabled, services.Settings.Theme); }
            catch (Exception ex) { services.LogContextFailure(ex); }
        });
        gameTimer.Tick += async (_, _) =>
        {
            if (gameSampling || closing || endingSession || exitRequested) return;
            gameSampling = true;
            try { await services.Hotkeys.SampleGameModeAsync(services.Settings.AutoGameMode,
                services.Settings.AutoGameMode && HotCornerNative.FullScreenGameRunning()); }
            catch (Exception ex) { services.LogContextFailure(ex); }
            finally { gameSampling = false; }
        };
        services.HotCorners.OpenPalette = () => palette?.Open();
        TrayPalette.KeyboardAcceleratorTextOverride = services.Settings.EffectiveShortcut(HotkeyCatalog.PaletteId).Shortcut;
        App.ViewModels.Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.PaletteShortcut))
                TrayPalette.KeyboardAcceleratorTextOverride = services.Settings.EffectiveShortcut(HotkeyCatalog.PaletteId).Shortcut;
        };
        focusTimer.Tick += async (_, _) => await UpdateFocusAsync();
        autosaveTimer.Tick += (_, _) => App.ViewModels.RefreshAutoSaveAge();
        Closed += (_, _) =>
        {
            services.QuickLook?.Dispose();
            services.HotCorners?.Dispose();
            palette?.Dispose();
            gameTimer.Stop(); gameFlyout?.Shutdown();
            sessionEnd.Dispose(); focusTimer.Stop(); autosaveTimer.Stop(); notifications.Dispose(); TrayIcon.Dispose();
            if (endingSession)
                _ = Task.Run(() => { services.Focus.Dispose(); services.Hotkeys.Dispose(); services.Dispose(); });
            else { services.Focus.Dispose(); services.Hotkeys.Dispose(); services.Dispose(); }
        };
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
        AppWindow.Changed += (_, args) =>
        {
            if (!initializing && (args.DidPositionChange || args.DidSizeChange) &&
                AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
                normalBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        };
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
            palette = new(services, App.ViewModels, DispatcherQueue, BringForward, ExitFromTrayAsync,
                notice => { App.ViewModels.ShowPaletteNotice(notice); if (!services.Hotkeys.GameMode && !notifications.ShowMessage(notice.Message)) BringForward(); },
                () => !initializing && !closing && !endingSession && !exitRequested);
            try { await palette.InitializeAsync(); }
            catch (Exception ex) { new CrashLog().TryWrite(CrashSource.Activation, ex); App.ViewModels.ShowStatus("Command palette shortcut could not start. Check Settings.", StatusKind.Warning); }
            if (services.Settings.QuickLookEnabled)
                await App.ViewModels.Settings.ApplyQuickLookAsync(true);
            if (services.Settings.HotCornersEnabled)
                await App.ViewModels.Settings.ApplyHotCornersAsync(true);
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
            App.ViewModels.Settings.RefreshPaletteSettings();
            try { TrayIcon.ForceCreate(enablesEfficiencyMode: false); trayReady = true; }
            catch (Exception) { App.ViewModels.ShowStatus("The tray icon could not be created. Keep this window open during focus.", StatusKind.Warning); }
            focusTimer.Start();
            App.ViewModels.AutoSave.Start();
            // Resuming after Start keeps the activation generation valid for its first published status.
            await App.ViewModels.ResumeActiveWorkspaceAsync();
            autosaveTimer.Start();
            var s = services.Settings;
            static ResumeBounds Bounds(RectInt32 r) => new(r.X, r.Y, r.Width, r.Height);
            var resume = WindowResumeRules.Bounds(s.X, s.Y, s.Width, s.Height, Bounds(DisplayArea.Primary.WorkArea),
                DisplayArea.FindAll().Select(d => Bounds(d.WorkArea)));
            normalBounds = new(resume.X, resume.Y, resume.Width, resume.Height);
            AppWindow.MoveAndResize(normalBounds);
            if (s.WindowMaximized && AppWindow.Presenter is OverlappedPresenter resumePresenter) resumePresenter.Maximize();
            var resumePage = Enum.Parse<AppPage>(WindowResumeRules.Page(s.LastPage));
            Navigation.IsEnabled = true; Navigate(resumePage);
            if (resumePage == AppPage.Capture)
            {
                App.ViewModels.Capture.Name = $"Workspace {services.Data.Workspaces.Count + 1}";
                await App.ViewModels.Capture.RefreshCommand.ExecuteAsync(null);
            }
            gameTimer.Start();
            await App.ViewModels.Calm.FirstCheckAsync();
            if (!services.Data.TourCompleted) App.ViewModels.RequestTour();
        }
        catch (Exception ex)
        {
            App.ViewModels.ShowStatus("Could not load workspaces. Check the data folder and restart. " + ex.Message,
                StatusKind.Error);
        }
        finally { initializing = false; }
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
        page = App.ViewModels.ResolvePage(page);
        if (loaded) services.Settings.LastPage = page.ToString();
        App.ViewModels.SetCurrentPage(page);
        App.ViewModels.Details.Workspace?.CommitName();
        navigating = true;
        try
        {
            var type = page switch
            {
                AppPage.Details => typeof(WorkspaceDetailsPage), AppPage.Capture => typeof(SaveOpenWindowsPage),
                AppPage.Settings => typeof(SettingsPage), AppPage.Layouts => typeof(LayoutsPage),
                AppPage.Advanced => typeof(AdvancedPage),
                AppPage.Focus => typeof(FocusPage), AppPage.Calm => typeof(CalmPage), AppPage.Hotkeys => typeof(HotkeysPage), _ => typeof(WorkspacesPage)
            };
            if (PageFrame.CurrentSourcePageType != type) PageFrame.Navigate(type);
            if (page == AppPage.Settings && App.ViewModels.FocusAdvancedSettingRequested && PageFrame.Content is SettingsPage settingsPage)
                settingsPage.FocusAdvancedSetting();
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
        if (initializing) { App.ViewModels.ShowStatus("Wait for FlowDesk to finish starting before closing."); return; }
        var action = UxDecisions.Close(exitRequested, endingSession, loaded, trayReady);
        if (action == CloseAction.Hide)
        {
            RememberWindow();
            bool wasShown = services.Settings.TrayCloseNoticeShown;
            if (!UxDecisions.CanHideWithTrayNotice(services.Settings, () => notifications.ShowMessage(FocusNotifications.TrayNotice)))
            {
                // Native inline fallback, once: never hide the only explanation when Windows cannot accept a toast.
                App.ViewModels.ShowStatus("FlowDesk keeps running in the tray when you close this window. Windows notifications are unavailable, so this window stayed open this time. Close it again to hide it, or use Exit in the tray menu to quit.", StatusKind.Information);
                services.Changed(true);
                return;
            }
            if (!wasShown) services.Changed(true);
            AppWindow.Hide();
            return;
        }
        if (App.ViewModels.IsBusy) { App.ViewModels.ShowStatus("Wait for the current operation to finish before closing."); return; }
        if (!loaded) { allowClose = true; Close(); return; }
        closing = true;
        PageFrame.IsEnabled = false; Navigation.IsEnabled = false;
        try
        {
            if (App.ViewModels.Advanced.HasActiveInstall)
            {
                if (!await advancedDialogs.WaitBeforeExitAsync())
                { closing = false; exitRequested = false; PageFrame.IsEnabled = true; Navigation.IsEnabled = true; return; }
                await App.ViewModels.Advanced.WaitForInstallAsync();
            }
            if (!exitRequested && !await App.ViewModels.Focus.EndSessionAsync(confirmAnyRunning: true))
            { closing = false; PageFrame.IsEnabled = true; Navigation.IsEnabled = true; return; }
        }
        catch (Exception)
        {
            closing = false; exitRequested = false; PageFrame.IsEnabled = true; Navigation.IsEnabled = true;
            App.ViewModels.ShowStatus("Close the open dialog before exiting FlowDesk.", StatusKind.Warning); return;
        }
        // Do not let a new focus session or autosave tick start while the final persistence await is in flight.
        bool tearingDown = false;
        try
        {
            PageFrame.IsEnabled = false; Navigation.IsEnabled = false; focusTimer.Stop(); autosaveTimer.Stop();
            await App.ViewModels.AutoSave.StopAsync();
            RememberWindow();
            await services.FlushAsync();
            if (!services.LastSaveSucceeded)
            {
                ResumeAfterFailedClose(); return;
            }
            // Once teardown starts, finish it even if one service fails. Never reopen an interactive
            // window with half its dependencies disposed or skip the later hook cleanup.
            tearingDown = true;
            void Cleanup(Action action) { try { action(); } catch (Exception ex) { services.LogContextFailure(ex); } }
            Cleanup(() => services.HotCorners?.Dispose());
            Cleanup(() => palette?.Dispose());
            Cleanup(() => { gameTimer.Stop(); gameFlyout?.Shutdown(); gameFlyout = null; });
            try { await Task.Run(() => services.Focus.Dispose()); } catch (Exception ex) { services.LogContextFailure(ex); }
            Cleanup(() => services.QuickLook?.Dispose());
            try { await services.Hotkeys.StopAllAsync(); } catch (Exception ex) { services.LogContextFailure(ex); }
            allowClose = true; Close();
        }
        catch (Exception ex)
        {
            services.LogContextFailure(ex);
            if (!tearingDown) ResumeAfterFailedClose();
            else { allowClose = true; closing = false; } // A second Close retries without touching disposed services.
            App.ViewModels.ShowStatus("FlowDesk could not finish exiting. Your window stayed open; try Exit again.", StatusKind.Error);
        }
    }
    private void ResumeAfterFailedClose()
    {
        allowClose = false; closing = false; exitRequested = false;
        PageFrame.IsEnabled = true; Navigation.IsEnabled = true;
        focusTimer.Start(); autosaveTimer.Start(); App.ViewModels.AutoSave.Start();
    }
    private void RememberWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) return;
        var s = services.Settings;
        s.WindowMaximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (!s.WindowMaximized) normalBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        else
        {
            static ResumeBounds Bounds(RectInt32 r) => new(r.X, r.Y, r.Width, r.Height);
            var target = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var relocated = WindowResumeRules.OnMonitor(Bounds(normalBounds), Bounds(target), DisplayArea.FindAll().Select(d => Bounds(d.WorkArea)));
            normalBounds = new(relocated.X, relocated.Y, relocated.Width, relocated.Height);
        }
        if (normalBounds.Width > 0 && normalBounds.Height > 0)
        {
            s.Width = normalBounds.Width; s.Height = normalBounds.Height;
            s.X = normalBounds.X; s.Y = normalBounds.Y;
        }
    }
    private void OnSessionEnd()
    {
        services.HotCorners?.Dispose();
        palette?.Dispose();
        endingSession = true; allowClose = true;
        gameTimer.Stop(); gameFlyout?.Shutdown(); gameFlyout = null;
        focusTimer.Stop(); autosaveTimer.Stop();
        // StopAsync invalidates the generation synchronously before its first await.
        _ = App.ViewModels.AutoSave.StopAsync();
        RememberWindow();
        if (loaded) services.FlushForSessionEnd();
        // No focus confirmation during shutdown, including strict sessions.
        _ = Task.Run(() => services.Focus.Stop(force: true));
        DispatcherQueue.TryEnqueue(Close);
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
            if (App.ViewModels.IsHotkeysPageOpen) App.ViewModels.Hotkeys.RefreshStatus();
            var vm = App.ViewModels.Focus;
            TrayIcon.ToolTipText = services.Hotkeys.GameMode ? "FlowDesk — game mode (shortcuts off)" : vm.IsRunning ? $"FlowDesk · {vm.RemainingText} remaining" : "FlowDesk";
            TrayPause.Text = vm.PauseLabel == "Resume" ? "Resume focus" : "Pause focus"; TrayPause.Visibility = vm.CanPause ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { services.LogContextFailure(ex); }
        finally { sampling = false; }
    }
    private async Task ExitFromTrayAsync()
    {
        if (allowClose) { Close(); return; }
        BringForward();
        if (App.ViewModels.IsBusy) { App.ViewModels.ShowStatus("Wait for the current operation to finish before exiting."); return; }
        if (closing || endingSession) return;
        // Gate new focus starts/navigation while the confirmation is open, not only during flush.
        closing = true; App.ViewModels.IsBusy = true;
        try
        {
            if (!await App.ViewModels.Focus.EndSessionAsync(confirmAnyRunning: true)) return;
            exitRequested = true;
        }
        catch (Exception)
        {
            App.ViewModels.ShowStatus("Close the open dialog before exiting FlowDesk.", StatusKind.Warning); return;
        }
        finally { closing = false; App.ViewModels.IsBusy = false; }
        Close();
    }
    private async Task ToggleGameModeAsync()
    {
        if (initializing || closing || endingSession || allowClose) return;
        try { await services.Hotkeys.ToggleGameModeAsync(); }
        catch (Exception ex) { services.LogContextFailure(ex); App.ViewModels.ShowStatus("Game mode could not be changed.", StatusKind.Warning); }
        finally { TrayGameMode.IsChecked = services.Hotkeys.GameMode; }
    }
}
