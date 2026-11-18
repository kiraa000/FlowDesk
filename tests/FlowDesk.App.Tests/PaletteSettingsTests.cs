using System.Text.Json;
using FlowDesk.App.Services;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Tests;

public sealed class PaletteSettingsTests
{
    [Fact] public void LoneWindowsKeyDefaultsOffAndPersists()
    {
        Assert.False(AppSettings.FromJson("{}").PaletteWindowsKey);
        Assert.True(AppSettings.FromJson(new AppSettings { PaletteWindowsKey = true }.ToJson()).PaletteWindowsKey);
    }
    [Fact] public void SourceSettingsDefaultOnAndRoundTripThroughAppSerializer()
    {
        var previous = AppSettings.FromJson("{\"Theme\":\"Dark\"}");
        Assert.True(previous.PaletteWindowsEnabled && previous.PaletteCalculatorEnabled && previous.PaletteSystemEnabled && previous.PaletteWebEnabled && previous.PaletteWebFallback);
        Assert.Equal(FlowDesk.Core.Palette.WebEngine.Google, previous.PaletteWebEngine);
        previous.PaletteWindowsEnabled = previous.PaletteCalculatorEnabled = previous.PaletteSystemEnabled = previous.PaletteWebEnabled = previous.PaletteWebFallback = false;
        previous.PaletteWebEngine = FlowDesk.Core.Palette.WebEngine.Custom;
        previous.PaletteWebTemplate = "https://example.com/?q=%s";
        var loaded = AppSettings.FromJson(previous.ToJson());
        Assert.False(loaded.PaletteWindowsEnabled || loaded.PaletteCalculatorEnabled || loaded.PaletteSystemEnabled || loaded.PaletteWebEnabled || loaded.PaletteWebFallback);
        Assert.Equal(previous.PaletteWebEngine, loaded.PaletteWebEngine);
        Assert.Equal(previous.PaletteWebTemplate, loaded.PaletteWebTemplate);
    }
    [Fact] public void OlderSettingsDefaultFileSearchOn()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":\"Dark\"}")!;
        Assert.True(settings.PaletteFilesEnabled);
    }
    [Fact] public void OlderSettingsDefaultPaletteOnIndependentlyOfWindowShortcuts()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"WindowShortcutsEnabled\":false}")!;
        Assert.True(settings.CommandPaletteEnabled); Assert.False(settings.WindowShortcutsEnabled);
    }
    [Fact] public void PaletteSettingAndHotkeysPreferenceShareOneTruthAndRoundTrip()
    {
        var settings = new AppSettings(); settings.CommandPaletteEnabled = false;
        Assert.Contains(HotkeyCatalog.PaletteId, settings.DisabledShortcuts);
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.False(loaded.CommandPaletteEnabled);
        loaded.DisabledShortcuts.Remove(HotkeyCatalog.PaletteId); Assert.True(loaded.CommandPaletteEnabled);
        loaded.CommandPaletteEnabled = true; loaded.CommandPaletteEnabled = true;
        Assert.Empty(loaded.DisabledShortcuts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PersistedSettingsContainOnlyThePaletteDisabledIdNotTheComputedProperty(bool enabled)
    {
        var settings = new AppSettings { CommandPaletteEnabled = enabled };
        string json = settings.ToJson();
        using var document = JsonDocument.Parse(json);
        Assert.DoesNotContain(document.RootElement.EnumerateObject(),
            property => property.Name.Equals(nameof(AppSettings.CommandPaletteEnabled), StringComparison.OrdinalIgnoreCase));
        var loaded = AppSettings.FromJson(json);
        Assert.Equal(enabled, loaded.CommandPaletteEnabled);
        Assert.Equal(!enabled, loaded.DisabledShortcuts.Contains(HotkeyCatalog.PaletteId));
    }

    [Theory]
    [InlineData("\"CommandPaletteEnabled\":true,\"DisabledShortcuts\":[\"command-palette\"]")]
    [InlineData("\"DisabledShortcuts\":[\"command-palette\"],\"CommandPaletteEnabled\":true")]
    public void LegacyComputedPropertyCannotOverrideDisabledIdRegardlessOfJsonOrder(string properties)
    {
        var loaded = AppSettings.FromJson("{" + properties + "}");
        Assert.False(loaded.CommandPaletteEnabled);
        Assert.Contains(HotkeyCatalog.PaletteId, loaded.DisabledShortcuts);
        Assert.False(AppSettings.FromJson(loaded.ToJson()).CommandPaletteEnabled);
    }
}
