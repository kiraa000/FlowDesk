using FlowDesk.App.Services;
using FlowDesk.Core.Palette;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class AiSettingsPanel : UserControl
{
    private readonly IAiCredentialStore credentials = new AiCredentialStore();
    private bool loading = true, busy;
    private CancellationTokenSource? test;
    public AiSettingsPanel()
    {
        InitializeComponent();
        var options = App.ViewModels.Services.Settings.Ai ?? new();
        ProviderBox.SelectedIndex = (int)options.Provider; EndpointBox.Text = options.BaseUrl ?? ""; ModelBox.Text = options.Model ?? "";
        FallbackSwitch.IsOn = options.ShowFallback; loading = false;
        ProviderBox.SelectionChanged += (_, _) =>
        {
            if (loading) return;
            ModelBox.Text = ProviderBox.SelectedIndex == 0 ? "claude-opus-5-5" : "gpt-4.1-mini";
            EndpointBox.IsEnabled = ProviderBox.SelectedIndex == 1; KeyBox.Password = ""; RefreshKeyStatus();
        };
        EndpointBox.IsEnabled = ProviderBox.SelectedIndex == 1;
        RefreshKeyStatus();
        Unloaded += (_, _) => test?.Cancel();
    }
    private AiOptions Options => new() { Provider = (AiProviderKind)ProviderBox.SelectedIndex, BaseUrl = EndpointBox.Text.Trim(), Model = ModelBox.Text.Trim(), ShowFallback = FallbackSwitch.IsOn };
    private bool SaveOptions()
    {
        if (AiRules.Validate(Options) is { } error) { ErrorText.Text = error; return false; }
        App.ViewModels.Services.Settings.Ai = Options; App.ViewModels.Services.Changed(); App.ViewModels.Settings.NotifyAiSettingsChanged(); ErrorText.Text = ""; return true;
    }
    private void Apply_Click(object sender, RoutedEventArgs e) { if (!busy && SaveOptions()) RefreshKeyStatus(); }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy || AiRules.Validate(Options) is { }) { ErrorText.Text = AiRules.Validate(Options) ?? "Wait for the test to finish."; return; }
        try { credentials.Save(Options.Provider, KeyBox.Password.Trim()); KeyBox.Password = ""; SaveOptions(); RefreshKeyStatus(); }
        catch (Exception) { KeyBox.Password = ""; ErrorText.Text = "The key could not be saved. Check it and try again."; }
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try { credentials.Remove(Options.Provider); KeyBox.Password = ""; SaveOptions(); App.ViewModels.Settings.NotifyAiSettingsChanged(); RefreshKeyStatus(); }
        catch (Exception) { ErrorText.Text = "The saved key could not be removed. Try again."; }
    }
    private void RefreshKeyStatus()
    {
        try
        {
            string? key = credentials.Read(Options.Provider);
            KeyStatus.Text = string.IsNullOrEmpty(key) ? AiRules.AllowsEmptyKey(Options) ? "Local endpoint · A key is optional" : "No key saved · AI answers are off" : "Key saved · …" + key[^Math.Min(4, key.Length)..];
        }
        catch (Exception) { KeyStatus.Text = "The saved key could not be read."; }
    }
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !SaveOptions()) return;
        busy = true; SaveButton.IsEnabled = RemoveButton.IsEnabled = ApplyButton.IsEnabled = TestButton.IsEnabled = false;
        ProviderBox.IsEnabled = ModelBox.IsEnabled = EndpointBox.IsEnabled = false;
        ErrorText.Text = "Testing only when you press Enter to confirm…";
        test = new();
        try
        {
            // A separate confirmation preserves the privacy promise: even Settings Test needs Enter.
            var confirm = new ContentDialog { XamlRoot = XamlRoot, Title = "Test AI provider", Content = "Press Enter on Send test to send a short test question to your selected provider.",
                PrimaryButtonText = "Send test", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            bool enter = false;
            confirm.PreviewKeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter && !args.KeyStatus.WasKeyDown) enter = true; };
            confirm.PrimaryButtonClick += (_, args) => { if (!enter) args.Cancel = true; enter = false; };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            ErrorText.Text = "Testing…";
            await foreach (var _ in new PaletteAiProvider(Options, credentials).StreamAsync("Reply with OK.", test.Token)) { }
            ErrorText.Text = "Connection succeeded.";
        }
        catch (OperationCanceledException) { ErrorText.Text = "Test cancelled or timed out."; }
        catch (AiAnswerException ex) { ErrorText.Text = ex.Message; }
        catch (Exception) { ErrorText.Text = "The provider could not be reached. Check AI settings."; }
        finally
        {
            test.Dispose(); test = null; busy = false;
            SaveButton.IsEnabled = RemoveButton.IsEnabled = ApplyButton.IsEnabled = TestButton.IsEnabled = ProviderBox.IsEnabled = ModelBox.IsEnabled = true;
            EndpointBox.IsEnabled = ProviderBox.SelectedIndex == 1; RefreshKeyStatus();
        }
    }
}
