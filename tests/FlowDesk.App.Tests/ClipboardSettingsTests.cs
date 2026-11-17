using System.Runtime.InteropServices;
using FlowDesk.App.Services;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Tests;

public sealed class ClipboardSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"WindowShortcutsEnabled\":true}")]
    [InlineData("{\"DisabledShortcuts\":[]}")]
    public void OldSettingsNeverOptIntoClipboard(string json) => Assert.False(AppSettings.FromJson(json).ClipboardHistoryEnabled);
    [Fact] public void OnlyOptInPreferenceIsPersisted()
    {
        var s = new AppSettings { ClipboardHistoryEnabled = true };
        Assert.True(AppSettings.FromJson(s.ToJson()).ClipboardHistoryEnabled);
        Assert.DoesNotContain("clipboard:", s.ToJson());
        s.ClipboardHistoryEnabled = false;
        Assert.False(AppSettings.FromJson(s.ToJson()).ClipboardHistoryEnabled);
    }
    [Fact] public void PasteInputMatchesX64AbiAndIsTagged()
    {
        Assert.Equal(40, Marshal.SizeOf<ClipboardInput>());
        Assert.Equal((nint)8, Marshal.OffsetOf<ClipboardInput>(nameof(ClipboardInput.Key)));
        Assert.Equal((nint)12, Marshal.OffsetOf<ClipboardInput>(nameof(ClipboardInput.Flags)));
        Assert.Equal((nint)24, Marshal.OffsetOf<ClipboardInput>(nameof(ClipboardInput.ExtraInfo)));
        var key = ClipboardInput.Keyboard(0x56, true);
        Assert.Equal(1u, key.Type); Assert.Equal((ushort)0x56, key.Key); Assert.Equal(2u, key.Flags);
        Assert.Equal(ShortcutKeyboardState.InjectionTag, key.ExtraInfo);
    }
    [Fact] public void ModeControlsStartHiddenAndClearHasAnAccessibleName()
    {
        var elements = PaletteMarkupFixture.Load().Descendants().ToArray();
        foreach (string name in new[] { "ClipboardModeLabel", "ClearClipboardButton" })
            Assert.Equal("Collapsed", (string?)elements.Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == name).Attribute("Visibility"));
    }
}
