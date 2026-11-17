using FlowDesk.App.Services;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Tests;

public sealed class ShortcutOverrideTests
{
    [Fact] public void OldFilesUseDefaultsAndChangedKeysPersistWithoutDefaultNoise()
    {
        var old = AppSettings.FromJson("{}"); Assert.Empty(old.ShortcutOverrides);
        Assert.Equal(HotkeyCatalog.Find(HotkeyCatalog.PaletteId), old.EffectiveShortcut(HotkeyCatalog.PaletteId));
        old.ShortcutOverrides[HotkeyCatalog.PaletteId] = "Ctrl+Alt+J";
        old.ShortcutOverrides["center"] = HotkeyCatalog.Find("center")!.Shortcut;
        var restored = AppSettings.FromJson(old.ToJson());
        Assert.Single(restored.ShortcutOverrides); Assert.Equal("Ctrl+Alt+J", restored.EffectiveShortcut(HotkeyCatalog.PaletteId).Shortcut);
        Assert.False(restored.CommandPaletteEnabled == false);
    }
    [Theory]
    [InlineData("{\"center\":42,\"minimize\":null,\"unknown\":\"Ctrl+J\",\"terminal-open\":\"Ctrl+Alt+J\"}")]
    [InlineData("[]")][InlineData("42")][InlineData("null")]
    public void InvalidOverrideEntriesDoNotLoseOtherSettings(string json)
    {
        var settings = AppSettings.FromJson("{\"Theme\":\"Dark\",\"ShortcutOverrides\":" + json + "}");
        Assert.Equal("Dark", settings.Theme);
        var issues = new List<string>(); settings.ShortcutOverrides = ShortcutRebinding.Normalize(settings.ShortcutOverrides, issues.Add);
        Assert.Equal(HotkeyCatalog.Find("center"), settings.EffectiveShortcut("center"));
        if (json.StartsWith('{')) { Assert.Equal("Ctrl+Alt+J", settings.EffectiveShortcut("terminal-open").Shortcut); Assert.Equal(3, issues.Count); }
    }
    [Theory]
    [InlineData(ShortcutProbeResult.Available, false)] [InlineData(ShortcutProbeResult.OwnedByFlowDesk, false)]
    [InlineData(ShortcutProbeResult.Conflict, true)] [InlineData(ShortcutProbeResult.Unavailable, true)]
    [InlineData(ShortcutProbeResult.HookUnknown, false)]
    public async Task FakeProbeResultsMapWithoutNativeCalls(ShortcutProbeResult result, bool blocks)
    {
        IShortcutProbe probe = new FakeProbe(result);
        var actual = await probe.ProbeAsync(new(2, 0x4A));
        Assert.Equal(blocks, ShortcutProbePresentation.Blocks(actual)); Assert.NotEmpty(ShortcutProbePresentation.Text(actual));
        if (actual == ShortcutProbeResult.HookUnknown) Assert.Contains("cannot be checked", ShortcutProbePresentation.Text(actual));
    }
    private sealed class FakeProbe(ShortcutProbeResult result) : IShortcutProbe
    { public Task<ShortcutProbeResult> ProbeAsync(KeyChord chord) => Task.FromResult(result); }
    [Fact] public void PaletteHintsAndTrayReadEffectiveShortcutAndTrackUpdates()
    {
        string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", path));
        Assert.Contains("Settings.EffectiveShortcut(HotkeyCatalog.PaletteId)", Read(Path.Combine("ViewModels", "PaletteSettings.cs")));
        Assert.Contains("Settings.EffectiveShortcut(HotkeyCatalog.ClipboardId)", Read(Path.Combine("ViewModels", "PaletteSettings.cs")));
        string tray = Read("MainWindow.xaml.cs");
        Assert.Contains("Settings.EffectiveShortcut(HotkeyCatalog.PaletteId)", tray);
        Assert.Contains("nameof(SettingsViewModel.PaletteShortcut)", tray);
        Assert.DoesNotContain("HotkeyCatalog.Find(", Read(Path.Combine("Views", "CommandPaletteWindow.xaml.cs")));
    }
}
