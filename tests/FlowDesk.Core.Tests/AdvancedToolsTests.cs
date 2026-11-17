using FlowDesk.Core.Advanced;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class AdvancedToolsTests
{
    private sealed class Reader : IToolDetectionReader
    {
        public List<ToolRegistration> Entries = [];
        public List<string> Paths = [];
        public List<string> Roots = [];
        public HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
        public IEnumerable<ToolRegistration> UninstallEntries() => Entries;
        public IEnumerable<string> AppPaths(string executable) => Paths;
        public IEnumerable<string> InstallRoots() => Roots;
        public bool FileExists(string path) => Files.Contains(path);
    }
    [Theory]
    [InlineData("Microsoft.PowerToys", @"D:\User\Local", @"Microsoft\PowerToys")]
    [InlineData("Microsoft.PowerToys", @"D:\Program Files", "PowerToys")]
    [InlineData("voidtools.Everything", @"D:\Program Files (x86)", "Everything")]
    [InlineData("ShareX.ShareX", @"D:\User\Local", @"Programs\ShareX")]
    public void KnownUserAndMachineFolders(string id, string root, string folder)
    {
        var tool = AdvancedTools.Get(id); var reader = new Reader();
        reader.Roots.Add(root); string path = Path.Combine(root, folder, tool.Executable); reader.Files.Add(path);
        Assert.Equal(path, ToolDetection.Find(tool, reader));
    }
    [Theory]
    [InlineData("Microsoft.PowerToys", "PowerToys (Preview)")]
    [InlineData("voidtools.Everything", "Everything 1.4.1")]
    [InlineData("ShareX.ShareX", "ShareX version 22.4.0")]
    public void UninstallRegistrationFindsCustomFolder(string id, string displayName)
    {
        var tool = AdvancedTools.Get(id); var reader = new Reader();
        reader.Entries.Add(new(displayName, @"D:\Custom", null));
        string path = Path.Combine(@"D:\Custom", tool.Executable); reader.Files.Add(path);
        Assert.Equal(path, ToolDetection.Find(tool, reader));
    }
    [Fact] public void StaleAppPathFallsThroughToExistingRegistration()
    {
        var tool = AdvancedTools.Get("ShareX.ShareX"); var reader = new Reader();
        reader.Paths.Add(@"D:\Gone\ShareX.exe");
        reader.Entries.Add(new("ShareX", null, "\"D:\\Custom\\ShareX.exe\",0"));
        reader.Files.Add(@"D:\Custom\ShareX.exe");
        Assert.Equal(@"D:\Custom\ShareX.exe", ToolDetection.Find(tool, reader));
    }
    [Fact] public void MissingAndWrongExecutableAreNotInstalled()
    {
        var tool = AdvancedTools.Get("ShareX.ShareX"); var reader = new Reader();
        reader.Paths.Add(@"D:\Other\uninstall.exe"); reader.Files.Add(reader.Paths[0]);
        reader.Entries.Add(new("ShareX helper", @"D:\Existing", null)); reader.Files.Add(@"D:\Existing\ShareX.exe");
        Assert.Null(ToolDetection.Find(tool, reader));
        reader.Paths.Add(@"D:\Gone\ShareX.exe"); Assert.Null(ToolDetection.Find(tool, reader));
    }
    [Fact] public void AppPathMustBeAbsoluteExistingExecutable()
    {
        var tool = AdvancedTools.Get("ShareX.ShareX"); var reader = new Reader();
        reader.Paths.Add("ShareX.exe"); reader.Files.Add("ShareX.exe"); Assert.Null(ToolDetection.Find(tool, reader));
        reader.Paths.Add("\"D:\\Custom\\ShareX.exe\""); reader.Files.Add(@"D:\Custom\ShareX.exe");
        Assert.Equal(@"D:\Custom\ShareX.exe", ToolDetection.Find(tool, reader));
    }
    [Fact] public void WingetArgumentsAreExactAndHiddenWithoutShell()
    {
        var info = ToolLaunch.Winget(@"D:\Tools\winget.exe", "ShareX.ShareX");
        Assert.Equal(@"D:\Tools\winget.exe", info.FileName);
        Assert.Equal(new[] { "install", "--id", "ShareX.ShareX", "-e", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" }, info.ArgumentList);
        Assert.False(info.UseShellExecute); Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput); Assert.True(info.RedirectStandardError);
        Assert.Throws<InvalidOperationException>(() => ToolLaunch.Winget("winget", "arbitrary.package"));
    }
    [Fact] public void WinUtilIsVisibleElevatedWindowsPowerShellWithNoPolicyBypass()
    {
        var info = ToolLaunch.WinUtil(@"D:\Windows");
        Assert.Equal(@"D:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", info.FileName);
        Assert.Equal("runas", info.Verb); Assert.True(info.UseShellExecute);
        Assert.False(info.CreateNoWindow); Assert.Equal(System.Diagnostics.ProcessWindowStyle.Normal, info.WindowStyle);
        Assert.Equal("-NoProfile -NoExit -Command \"irm 'https://christitus.com/win' | iex\"", info.Arguments);
        Assert.DoesNotContain("ExecutionPolicy", info.Arguments);
    }
    [Theory]
    [InlineData(0u, WingetOutcome.Success)]
    [InlineData(0x8A15002Bu, WingetOutcome.AlreadyInstalled)]
    [InlineData(0x8A150061u, WingetOutcome.AlreadyInstalled)]
    [InlineData(0x8A15010Du, WingetOutcome.AlreadyInstalled)]
    [InlineData(0x800704C7u, WingetOutcome.Cancelled)]
    [InlineData(0x8A15010Cu, WingetOutcome.Cancelled)]
    [InlineData(0x8A150005u, WingetOutcome.Cancelled)]
    [InlineData(0x80072EE7u, WingetOutcome.Network)]
    [InlineData(0x8A150107u, WingetOutcome.Network)]
    [InlineData(0x8A150014u, WingetOutcome.NotFound)]
    [InlineData(0x8A15002Cu, WingetOutcome.Failed)]
    [InlineData(0x8A15001Bu, WingetOutcome.Failed)]
    [InlineData(0xDEADBEEFu, WingetOutcome.Failed)]
    public void MapsDocumentedExitCodes(uint code, WingetOutcome expected) => Assert.Equal(expected, ToolLaunch.Outcome(unchecked((int)code)));
    [Fact] public void UnknownCodeKeepsHexForDiagnosis() => Assert.Equal("Install failed (code 0xDEADBEEF).", ToolLaunch.ResultText(unchecked((int)0xDEADBEEF)));
    [Fact] public void EveryOffToOnRequiresConsent()
    {
        Assert.True(AdvancedConsent.NeedsAgreement(false, true));
        Assert.False(AdvancedConsent.NeedsAgreement(true, false));
        Assert.False(AdvancedConsent.NeedsAgreement(true, true));
        Assert.False(AdvancedConsent.NeedsAgreement(false, false));
        Assert.True(AdvancedConsent.NeedsAgreement(false, true));
    }
    [Fact] public async Task CancelNeverEnablesOrReplacesAuditTime()
    {
        var previous = DateTimeOffset.Parse("2026-11-16T10:00:00Z");
        int asks = 0;
        var pending = new TaskCompletionSource<bool>();
        var change = AdvancedConsent.RequestAsync(false, previous, true, () => { asks++; return pending.Task; }, () => throw new Exception("Cancel must not take a new timestamp"));
        Assert.False(change.IsCompleted); Assert.Equal(1, asks);
        pending.SetResult(false);
        Assert.Equal(new AdvancedModeDecision(false, previous), await change);
    }
    [Fact] public async Task EveryReenableAsksAndOnlyAcceptanceUpdatesTimestamp()
    {
        int asks = 0;
        var first = DateTimeOffset.Parse("2026-11-17T10:00:00Z");
        var second = first.AddMinutes(5);
        Task<bool> Agree() { asks++; return Task.FromResult(true); }
        var on = await AdvancedConsent.RequestAsync(false, null, true, Agree, () => first);
        Assert.True(on.Enabled); Assert.Equal(first, on.AgreedAt);
        var same = await AdvancedConsent.RequestAsync(on.Enabled, on.AgreedAt, true, Agree, () => second);
        Assert.Equal(on, same); Assert.Equal(1, asks);
        var off = await AdvancedConsent.RequestAsync(on.Enabled, on.AgreedAt, false, () => throw new Exception("Off never asks"), () => second);
        Assert.False(off.Enabled); Assert.Equal(first, off.AgreedAt);
        var again = await AdvancedConsent.RequestAsync(off.Enabled, off.AgreedAt, true, Agree, () => second);
        Assert.True(again.Enabled); Assert.Equal(second, again.AgreedAt); Assert.Equal(2, asks);
    }
    [Fact] public void NavigationAndPaletteAreOptIn()
    {
        var pages = new Dictionary<string, bool> { ["Advanced"] = false };
        Assert.False(OptionalNavigation.Visible("Advanced", pages));
        Assert.Equal("Workspaces", OptionalNavigation.Resolve("Advanced", "Workspaces", pages));
        Assert.Empty(AdvancedTools.PaletteTools(false, AdvancedTools.All.Select(t => t.Id)));
        Assert.Empty(AdvancedTools.PaletteTools(true, []));
        Assert.Single(AdvancedTools.PaletteTools(true, ["ShareX.ShareX", "unlisted"]));
        pages["Advanced"] = true;
        Assert.True(OptionalNavigation.Visible("Advanced", pages));
        Assert.Equal("Advanced", OptionalNavigation.Resolve("Advanced", "Workspaces", pages));
        Assert.Equal(3, AdvancedTools.PaletteTools(true, AdvancedTools.All.Select(t => t.Id)).Count());
    }
}
