using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Services;
using FlowDesk.Core.Advanced;

namespace FlowDesk.App.ViewModels;

public sealed class ToolCardViewModel : ObservableObject
{
    internal AdvancedTool Tool { get; }
    internal string? ExePath { get; set; }
    internal string? Message { get; set; }
    internal bool Installing { get; set; }
    internal bool InstallCompleted { get; set; }
    private readonly AdvancedViewModel owner;
    public ToolCardViewModel(AdvancedTool tool, AdvancedViewModel owner)
    {
        Tool = tool; this.owner = owner;
        OpenCommand = new AsyncRelayCommand(() => owner.OpenAsync(this));
        InstallCommand = new AsyncRelayCommand(() => owner.InstallAsync(this));
        GetWingetCommand = new RelayCommand(() => owner.OpenStore(this));
    }
    public string Name => Tool.Name;
    public string OpenName => "Open " + Name;
    public string InstallName => "Install " + Name;
    public string WebsiteName => Name + " official website";
    public string Description => Tool.Description;
    public string Publisher => "Publisher: " + Tool.Publisher;
    public Uri Website => new(Tool.Website);
    public bool Installed => ExePath is not null;
    public bool IsInstalling => Installing;
    public bool CanOpen => Installed && owner.Enabled;
    public bool ShowInstall => !Installed && owner.DetectionCompleted && !NeedsWinget;
    public bool NeedsWinget => !Installed && owner.DetectionCompleted && !owner.WingetAvailable;
    public bool CanInstall => owner.Enabled && !owner.InstallBusy && owner.WingetAvailable;
    public string InstallHint => owner.InstallBusy ? "Another installation is pending or running. Wait for it to finish." : "Install from the official winget source";
    public string Status => Installing ? "Installing…" : Message ?? (Installed ? "Installed" : InstallCompleted ? "Install finished, but the program was not found. Reopen this page to check again." : !owner.DetectionCompleted ? "Checking installation…" : NeedsWinget ? "Install needs App Installer (winget)" : owner.InstallBusy ? "Not installed. Waiting for the other installation." : "Not installed");
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand InstallCommand { get; }
    public IRelayCommand GetWingetCommand { get; }
    internal void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed partial class AdvancedViewModel : ObservableObject
{
    private readonly AppViewModels owner;
    private readonly AdvancedToolService service;
    private long detectionGeneration;
    private bool installBusy;
    private bool winUtilBusy;
    private Task? activeInstall;
    public AdvancedViewModel(AppViewModels owner)
    {
        this.owner = owner; service = new(owner.Services.Store.Folder);
        Tools = AdvancedTools.All.Select(t => new ToolCardViewModel(t, this)).ToArray();
    }
    public IReadOnlyList<ToolCardViewModel> Tools { get; }
    public bool Enabled => owner.Services.Settings.AdvancedModeEnabled;
    public bool WingetAvailable => wingetPath is not null;
    public bool DetectionCompleted { get; private set; }
    private string? wingetPath;
    public bool InstallBusy => installBusy;
    public bool HasActiveInstall => activeInstall is { IsCompleted: false };
    public Func<Task<bool>>? ConfirmEnable { get; set; }
    public Func<AdvancedTool, Task<bool>>? ConfirmInstall { get; set; }
    public Func<Task<bool>>? ConfirmWinUtil { get; set; }
    [ObservableProperty] public partial string WinUtilStatus { get; set; } = "";
    public async Task WaitForInstallAsync()
    {
        try { if (activeInstall is { } task) await task; }
        catch (Exception) { /* The owning InstallAsync surfaces failure on its card, not as a close failure. */ }
    }
    public async Task RequestEnabledAsync(bool requested)
    {
        if (requested == Enabled) return;
        var decision = await AdvancedConsent.RequestAsync(Enabled, owner.Services.Settings.AdvancedModeAgreedAt, requested,
            () => ConfirmEnable?.Invoke() ?? Task.FromResult(false), () => DateTimeOffset.UtcNow);
        if (decision.Enabled == Enabled) return;
        owner.Services.Settings.AdvancedModeEnabled = decision.Enabled;
        owner.Services.Settings.AdvancedModeAgreedAt = decision.AgreedAt;
        owner.Services.Changed();
        OnPropertyChanged(nameof(Enabled));
        owner.RefreshAdvancedPages(); RefreshCards();
        if (requested) await RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        var generation = ++detectionGeneration;
        try
        {
            var snapshot = await service.DetectAsync();
            if (generation != detectionGeneration) return;
            wingetPath = snapshot.WingetPath;
            DetectionCompleted = true;
            foreach (var card in Tools) card.ExePath = snapshot.Paths[card.Tool.Id];
            RefreshCards();
        }
        catch (Exception ex)
        {
            if (generation != detectionGeneration) return;
            foreach (var card in Tools) card.Message = "Could not check installation: " + ex.Message;
            RefreshCards();
        }
    }
    private void RefreshCards() { foreach (var card in Tools) card.Refresh(); }
    internal async Task OpenAsync(ToolCardViewModel card)
    {
        if (!Enabled) return;
        try
        {
            if (card.ExePath is null) throw new FileNotFoundException("Program not found. Checking installation again.");
            AdvancedToolService.Open(card.Tool, card.ExePath); card.Message = null;
        }
        catch (Exception ex) { card.Message = "Could not open: " + ex.Message; await RefreshAsync(); }
        card.Refresh();
    }
    internal void OpenStore(ToolCardViewModel card)
    {
        if (!Enabled) return;
        try { AdvancedToolService.OpenLink(ToolLaunch.StoreUri); }
        catch (Exception ex) { card.Message = "Could not open Microsoft Store: " + ex.Message; card.Refresh(); }
    }
    internal async Task InstallAsync(ToolCardViewModel card)
    {
        // Reserve the single install slot before awaiting consent, not after it.
        if (!Enabled || owner.IsBusy || installBusy || wingetPath is null || card.Installed) return;
        installBusy = true; RefreshCards();
        try
        {
            if (ConfirmInstall is null || !await ConfirmInstall(card.Tool) || !Enabled || owner.IsBusy) return;
            card.Installing = true; card.InstallCompleted = false; card.Message = null; RefreshCards();
            activeInstall = CompleteInstallAsync(card, wingetPath);
            await activeInstall;
        }
        catch (Exception ex) { card.Message = "Install failed: " + ex.Message; }
        finally { card.Installing = false; installBusy = false; activeInstall = null; RefreshCards(); }
    }
    private async Task CompleteInstallAsync(ToolCardViewModel card, string winget)
    {
        int code = await service.InstallAsync(winget, card.Tool);
        card.Message = ToolLaunch.ResultText(code);
        await RefreshAsync();
        if (ToolLaunch.Outcome(code) is WingetOutcome.Success or WingetOutcome.AlreadyInstalled)
        {
            // Derive the missing-program notice from the live path. A newer page-open detection may
            // supersede this refresh; its later result must clear the notice simply by finding the exe.
            card.InstallCompleted = true; card.Message = null;
        }
    }
    [RelayCommand]
    private async Task RunWinUtilAsync()
    {
        if (!Enabled || owner.IsBusy || winUtilBusy) return;
        winUtilBusy = true;
        try
        {
            if (ConfirmWinUtil is null || !await ConfirmWinUtil() || !Enabled || owner.IsBusy) return;
            AdvancedToolService.RunWinUtil(); WinUtilStatus = "PowerShell opened. Continue in its window.";
        }
        catch (Exception ex) { WinUtilStatus = AdvancedToolService.IsUacDeclined(ex) ? "Administrator permission declined." : "Could not run WinUtil: " + ex.Message; }
        finally { winUtilBusy = false; }
    }
}
