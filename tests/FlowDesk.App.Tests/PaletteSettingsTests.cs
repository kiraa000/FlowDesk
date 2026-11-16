using System.Text.Json;
using FlowDesk.App.Services;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Tests;

public sealed class PaletteSettingsTests
{
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
}
