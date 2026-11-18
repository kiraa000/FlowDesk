using System.Text.RegularExpressions;

namespace FlowDesk.App.Tests;

public sealed class PaletteHotkeyRoutingTests
{
    private static string Source => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", "Services", "HotkeyHost.cs"));

    // Catches the exact R3 failure: a posted private id without a WndProc arm.
    private static string[] UnhandledPrivatePosts(string source)
    {
        string wndProc = source[source.IndexOf("private static nint WndProc(", StringComparison.Ordinal)..];
        var privateIds = Regex.Matches(source, @"const uint (WM_\w+)\s*=\s*0x8[0-9A-Fa-f]+")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return Regex.Matches(source, @"PostMessageW\([^,]+,\s*(WM_\w+)")
            .Select(m => m.Groups[1].Value).Where(privateIds.Contains).Distinct()
            .Where(id => !Regex.IsMatch(wndProc, @"message\s*==\s*" + id + @"\b")).ToArray();
    }

    [Fact] public void AllPostedPrivateMessagesHaveWndProcHandlersAndNativeDeliveryRemains()
    {
        string source = Source;
        Assert.Empty(UnhandledPrivatePosts(source));
        Assert.DoesNotContain("WM_OWNED_HOTKEY", source);
        Assert.Contains("if (message == WM_HOTKEY", source);
        Assert.Contains("host.OnHotkey((int)wParam)", source);
        Assert.Contains("swallow |= owned.Swallow", source);
        Assert.Contains("ConfigurePaletteWindowsKeyAsync", source);
    }

    [Fact] public void ContractWouldRejectR3UnroutedReplacementMessage()
    {
        const string broken = "private const uint WM_OWNED_HOTKEY = 0x800F; " +
            "PostMessageW(_hwnd, WM_OWNED_HOTKEY, id, 0); private static nint WndProc() { if (message == WM_HOTKEY) OnHotkey(); }";
        Assert.Equal(new[] { "WM_OWNED_HOTKEY" }, UnhandledPrivatePosts(broken));
    }

    [Fact] public void PaletteHookFailuresRetainNativeRegistrationsAndMouseChangeIsHandled()
    {
        string source = Source;
        Assert.DoesNotContain("throw new InvalidOperationException(\"Shortcut key-pair safety hook", source);
        Assert.DoesNotContain("throw new InvalidOperationException(\"Palette mouse-observation hook", source);
        Assert.Contains("ApplyWindowsKeyHookChange()", source);
        Assert.Contains("ReleaseUnusedMouseHook()", source);
        Assert.Contains("native-hotkeys-retained=true", source);
    }
}
