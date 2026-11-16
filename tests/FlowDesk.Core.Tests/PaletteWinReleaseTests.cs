using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class PaletteWinReleaseTests
{
    [Theory]
    [InlineData(0x5B, true)]
    [InlineData(0x5B, false)]
    [InlineData(0x5C, true)]
    [InlineData(0x5C, false)]
    public void RegisteredPaletteChordDoesNotArmWheelGestureWinUpReplacement(int win, bool altFirst)
    {
        var keyboard = new ShortcutKeyboardState();
        keyboard.Observe(win, true, 0);
        keyboard.Observe(0xA4, true, 0);
        Assert.True(keyboard.Modifiers.Win && keyboard.Modifiers.Alt);
        keyboard.Observe(0x20, true, 0);
        // The registered WM_HOTKEY path dispatches the palette, not AcceptWinGesture (wheel only).
        keyboard.Observe(0x20, false, 0);
        if (altFirst) keyboard.Observe(0xA4, false, 0);
        var release = keyboard.Observe(win, false, 0);
        Assert.True(release.WinKeyUp);
        Assert.False(release.GestureAccepted);
        var result = WinKeyRelease.Process((ushort)win, release.GestureAccepted, false,
            _ => throw new InvalidOperationException("Registered hotkeys must not inject replacement keys"));
        Assert.False(result.Attempted);
        Assert.False(result.Swallow);
        if (!altFirst) keyboard.Observe(0xA4, false, 0);
        Assert.False(keyboard.Modifiers.Win || keyboard.Modifiers.Alt);
    }
}
