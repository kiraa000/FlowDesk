using System.Text;
using FlowDesk.App.Services;
using FlowDesk.Core.Storage;

namespace FlowDesk.App.Tests;

public sealed class GameModeSettingsTests
{
    // Same source-copy pattern as Task031WiringTests; structural guard only, no native hooks.
    private static string Source(params string[] path) => File.ReadAllText(Path.Combine(
        new[] { AppContext.BaseDirectory, "AppSources" }.Concat(path).ToArray()));

    [Fact]
    public void GameModeBoundarySeedsFromHookStateBeforeTheGateReset()
    {
        var host = Source("Services", "HotkeyHost.cs");
        var start = host.IndexOf("private void ApplyBindingChange()", StringComparison.Ordinal);
        var end = host.IndexOf("private sealed record HotCornerHookChange", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var apply = host[start..end];
        var seed = apply.IndexOf(
            "_boundaryHeld.Seed(key => _repeatGate.IsDown(key) || _boundaryHeld.Contains((uint)key));",
            StringComparison.Ordinal);
        Assert.True(seed >= 0, "the game-mode boundary must seed hook state unioned with earlier held keys");
        Assert.True(apply.IndexOf("_repeatGate.Reset();", StringComparison.Ordinal) > seed,
            "the held set must be sampled before the repeat gate is reset");
        Assert.True(apply.IndexOf("_inputOwnership.Reset();", StringComparison.Ordinal) > seed);
        Assert.DoesNotContain("_boundaryHeld.Seed(IsDown);", apply);
    }

    [Fact]
    public void NewSettingsRoundTripAndRuntimeModeIsNotStored()
    {
        var settings = new AppSettings { AutoGameMode = true, WindowMaximized = true, LastPage = "Hotkeys", X = -1500, Width = 1000 };
        var json = settings.ToJson(); var copy = AppSettings.FromJson(json);
        Assert.True(copy.AutoGameMode); Assert.True(copy.WindowMaximized); Assert.Equal("Hotkeys", copy.LastPage);
        Assert.Equal(-1500, copy.X); Assert.Equal(1000, copy.Width);
        Assert.DoesNotContain("\"GameMode\"", json);
    }
    [Fact]
    public void TransferKeepsNewKeysAndAcceptsOldFiles()
    {
        var settings = new AppSettings { AutoGameMode = true, WindowMaximized = true, LastPage = "Settings" };
        var exported = SettingsTransfer.Export(new FlowDeskData(), settings, new(), "test");
        var preview = SettingsTransfer.Parse(Encoding.UTF8.GetBytes(exported));
        Assert.Null(preview.Error); Assert.All(preview.Sections, s => Assert.Null(s.Error));
        var merged = SettingsTransfer.Merge(new(), new(), preview, new());
        Assert.True(merged.Settings.AutoGameMode); Assert.True(merged.Settings.WindowMaximized); Assert.Equal("Settings", merged.Settings.LastPage);
        var old = SettingsTransfer.Parse(Encoding.UTF8.GetBytes("""{"format":"flowdesk-export","version":1,"sections":{"settings":{"Theme":"System"}}}"""));
        Assert.Null(old.Error); Assert.All(old.Sections, s => Assert.Null(s.Error));
        Assert.False(SettingsTransfer.Merge(new(), new(), old, new()).Settings.AutoGameMode);
    }
    [Theory]
    [InlineData("\"AutoGameMode\":\"yes\"")]
    [InlineData("\"WindowMaximized\":1")]
    [InlineData("\"LastPage\":\"Unknown\"")]
    public void InvalidImportedValuesAreRejected(string field)
    {
        var preview = SettingsTransfer.Parse(Encoding.UTF8.GetBytes("{\"format\":\"flowdesk-export\",\"version\":1,\"sections\":{\"settings\":{" + field + "}}}"));
        Assert.NotNull(Assert.Single(preview.Sections).Error);
    }
    [Fact]
    public async Task RuntimeStartsOffAndTogglesSerialize()
    {
        using var service = new HotkeyService("unused");
        Assert.False(service.GameMode);
        await Task.WhenAll(service.ToggleGameModeAsync(), service.ToggleGameModeAsync());
        Assert.False(service.GameMode);
        await service.SetGameModeAsync(true);
        await service.SampleGameModeAsync(true, false);
        Assert.True(service.GameMode);
    }
}
