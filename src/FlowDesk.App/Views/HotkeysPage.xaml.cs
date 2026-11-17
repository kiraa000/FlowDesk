using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Views;

public sealed partial class HotkeysPage : Page
{
    private bool dialogOpen;
    internal IShortcutProbe Probe { get; set; } = new ShortcutProbe();
    public HotkeysPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Hotkeys;
        Loaded += (_, _) =>
        {
            App.ViewModels.Hotkeys.RefreshStatus();
            var command = App.ViewModels.Hotkeys.RefreshCommand;
            if (command.CanExecute(null)) command.Execute(null);
        };
    }
    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || App.ViewModels.Hotkeys.IsBusy) return;
        dialogOpen = true;
        try
        {
            var confirm = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                Title = "Reset all shortcuts?", Content = "Restore the default keys for every shortcut. Your on/off choices will be kept.",
                PrimaryButtonText = "Reset all shortcuts", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                var error = await App.ViewModels.Hotkeys.ResetAllAsync();
                if (error is not null) App.ViewModels.ShowStatus(error, StatusKind.Warning);
            }
        }
        finally { dialogOpen = false; (sender as Control)?.Focus(FocusState.Programmatic); }
    }
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || sender is not Button button || button.DataContext is not HotkeyRowViewModel row || !row.CanEdit) return;
        dialogOpen = true;
        try { await EditAsync(row); }
        catch (Exception ex) { App.ViewModels.ShowStatus(ex.Message, StatusKind.Warning); }
        finally { dialogOpen = false; button.Focus(FocusState.Programmatic); }
    }
    private async Task EditAsync(HotkeyRowViewModel row)
    {
        var settings = App.ViewModels.Services.Settings;
        var state = new ShortcutCapture();
        KeyChord candidate = KeyChord.From(row.Binding);
        int version = 0;
        bool closed = false, checking = false, saving = false;
        ShortcutConflictResult check = App.ViewModels.Hotkeys.CheckCandidate(row.Binding.Id, candidate);
        ShortcutProbeResult probeResult = ShortcutProbeResult.Unavailable;
        var stack = new StackPanel { Spacing = 12, MinWidth = 320, MaxWidth = 440 };
        stack.Children.Add(Ui.Text(row.Label));
        stack.Children.Add(Ui.Text("Press the new shortcut", "BodyStrongTextBlockStyle"));
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var capture = new Button { Content = caps, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 56 };
        AutomationProperties.SetName(capture, "Press the new shortcut. Tab moves to conflict choices and Save; Escape cancels.");
        stack.Children.Add(capture);
        var heldText = Ui.Text("Select the box to capture keys. Tab moves to the dialog controls.", "CaptionTextBlockStyle", secondary: true);
        stack.Children.Add(heldText);
        var feedback = Ui.Text("");
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        stack.Children.Add(feedback);
        var resolution = new ComboBox { Header = "FlowDesk conflict", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(resolution, "Resolve FlowDesk shortcut conflict");
        stack.Children.Add(resolution);
        var reset = new Button { Content = "Reset to default" };
        stack.Children.Add(reset);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "Change shortcut",
            Content = stack, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.None, IsPrimaryButtonEnabled = false };
        void DrawCaps(KeyChord? display = null)
        {
            caps.Children.Clear();
            foreach (string part in (display ?? candidate).ToString().Split('+'))
            {
                var cap = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Padding='8,4' BorderThickness='1' CornerRadius='4' BorderBrush='{ThemeResource ControlStrokeColorDefaultBrush}' Background='{ThemeResource ControlFillColorDefaultBrush}' />");
                cap.Child = Ui.Text(part, "CaptionTextBlockStyle"); caps.Children.Add(cap);
            }
            AutomationProperties.SetName(capture, "New shortcut: " + candidate + ". Tab moves to Save; Escape cancels.");
        }
        void EnableSave() => dialog.IsPrimaryButtonEnabled = !closed && !saving && !checking && !check.Blocked && !state.MultipleKeys &&
            !ShortcutProbePresentation.Blocks(probeResult) && (check.Others.Count == 0 || resolution.SelectedIndex > 0);
        async Task ValidateAsync()
        {
            int request = ++version;
            checking = true; EnableSave(); DrawCaps();
            check = App.ViewModels.Hotkeys.CheckCandidate(row.Binding.Id, candidate);
            resolution.Items.Clear(); resolution.SelectedIndex = -1;
            resolution.Visibility = check.Others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (check.Others.Count > 0)
            {
                resolution.Items.Add("Choose a resolution");
                resolution.Items.Add("Use here and turn off " + string.Join(", ", check.Others.Select(b => b.Label)));
                if (check.Others.Count == 1) resolution.Items.Add("Swap with " + check.Others[0].Label);
                resolution.SelectedIndex = 0;
            }
            string windows = check.Windows is null ? "" : check.Windows.Kind == WindowsShortcutKind.Reserved
                ? $"Reserved by Windows: {check.Windows.Action}." : $"Replaces a Windows shortcut: {check.Windows.Action}.";
            string conflict = check.Others.Count == 0 ? "" : "Already used by FlowDesk: " + string.Join(", ", check.Others.Select(b => b.Label)) + ".";
            feedback.Text = string.Join("\n", new[] { check.Error, windows, conflict, "Checking availability…" }.Where(s => !string.IsNullOrEmpty(s)));
            // Our current registered chords would fail a native probe against ourselves.
            bool owned = ShortcutRebinding.Effective(settings.ShortcutOverrides).Any(b => !b.IsMouseWheel && KeyChord.From(b) == candidate &&
                App.ViewModels.Services.Hotkeys.ShortcutRuntime.Registration.Registered.Contains(b.Id));
            var result = check.Blocked ? ShortcutProbeResult.Unavailable : owned ? ShortcutProbeResult.OwnedByFlowDesk : await Probe.ProbeAsync(candidate);
            if (closed || request != version) return;
            probeResult = result; checking = false;
            feedback.Text = string.Join("\n", new[] { check.Error, windows, conflict, check.Blocked ? "" : ShortcutProbePresentation.Text(result) }.Where(s => !string.IsNullOrEmpty(s)));
            EnableSave();
        }
        resolution.SelectionChanged += (_, _) => EnableSave();
        reset.Click += async (_, _) => { state.ResetHeld(); candidate = KeyChord.From(HotkeyCatalog.Find(row.Binding.Id)!); await ValidateAsync(); };
        using var hook = new ShortcutCaptureHook(Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId), (key, down) =>
        {
            state.Observe(key, down);
            var held = state.Held;
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (closed || saving) return;
                heldText.Text = state.MultipleKeys ? "Press just one non-modifier key at a time." : held.ToString().Length == 0 ? "Keys released · Tab moves to Save" : "Held: " + held;
                if (down)
                {
                    candidate = state.Candidate ?? default;
                    await ValidateAsync();
                }
                else EnableSave();
                DrawCaps(state.Held != default ? state.Held : candidate);
            });
        }, () => DispatcherQueue.TryEnqueue(() => Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(FocusNavigationDirection.Previous)), state.ResetHeld);
        capture.GotFocus += (_, _) => { state.ResetHeld(); hook.Armed = true; };
        capture.LostFocus += (_, _) => { hook.Armed = false; state.ResetHeld(); };
        dialog.Opened += async (_, _) => { capture.Focus(FocusState.Programmatic); await ValidateAsync(); };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (!dialog.IsPrimaryButtonEnabled) return;
            var deferral = args.GetDeferral();
            saving = true; hook.Armed = false; EnableSave(); reset.IsEnabled = false; resolution.IsEnabled = false; capture.IsEnabled = false;
            try
            {
                var choice = resolution.SelectedIndex == 2 ? ShortcutResolution.Swap : resolution.SelectedIndex == 1 ? ShortcutResolution.TurnOtherOff : ShortcutResolution.None;
                string? error = await App.ViewModels.Hotkeys.SaveEditAsync(row.Binding.Id, candidate, choice);
                if (error is null) args.Cancel = false;
                else feedback.Text = error;
            }
            finally { saving = false; reset.IsEnabled = true; resolution.IsEnabled = true; capture.IsEnabled = true; EnableSave(); deferral.Complete(); }
        };
        try { await dialog.ShowAsync(); }
        finally { closed = true; ++version; hook.Armed = false; }
    }
}
