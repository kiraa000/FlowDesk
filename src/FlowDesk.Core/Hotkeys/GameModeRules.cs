namespace FlowDesk.Core.Hotkeys;

/// <summary>Runtime ownership: manual-on persists; manual-off suppresses auto-on until the game ends.</summary>
public sealed record GameModeState(bool Manual = false, bool Automatic = false, bool SuppressAutomatic = false, bool GameRunning = false)
{
    public bool Enabled => Manual || Automatic;
    public GameModeState SetManual(bool enabled) => new(enabled, false, !enabled && GameRunning, GameRunning);
    public GameModeState Sample(bool option, bool game) => this with
    {
        Automatic = option && game && !SuppressAutomatic,
        SuppressAutomatic = option && game && SuppressAutomatic,
        GameRunning = option && game
    };
}

public static class GameModeRules
{
    public static bool Retained(string id) => id is HotkeyCatalog.PaletteId or HotkeyCatalog.GameModeToggleId;
    public static bool PassHooks(bool gameMode, bool lonePalette) => gameMode && !lonePalette;
    public static IReadOnlyList<string> TurnAllOffIds => HotkeyCatalog.All.Where(b => !Retained(b.Id)).Select(b => b.Id).ToArray();
}

/// <summary>Inputs already held at a boundary cannot become fresh shortcuts on an auto-repeat.</summary>
public sealed class GameModeInputBoundary
{
    private readonly HashSet<uint> held = [];
    public void Seed(Func<int, bool> isDown)
    {
        var snapshot = Enumerable.Range(1, 255).Where(isDown).Select(k => (uint)k).ToArray();
        held.Clear(); held.UnionWith(snapshot);
    }
    public bool Contains(uint key) => held.Contains(key);
    public bool Observe(uint key, bool down)
    {
        bool boundary = held.Contains(key);
        if (!down) held.Remove(key);
        return boundary;
    }
}

public readonly record struct GameModeFlyoutBounds(int X, int Y, int Width, int Height);
public sealed class GameModeFlyoutLifetime
{
    private long generation;
    public long Deadline { get; private set; }
    public long Restart(long now, int fadeInMilliseconds = 0)
    {
        Deadline = GameModeFlyoutRules.Deadline(now + fadeInMilliseconds);
        return ++generation;
    }
    public bool IsCurrent(long epoch) => epoch == generation;
}
public static class GameModeFlyoutRules
{
    public const int VisibleMilliseconds = 1500;
    public static long Deadline(long now) => now + VisibleMilliseconds;
    public static GameModeFlyoutBounds Place(int x, int y, int width, int height, double scale, bool gameMode = true, double textScale = 1)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        textScale = double.IsFinite(textScale) ? Math.Clamp(textScale, 1, 3) : 1;
        int w = Math.Min(width, (int)Math.Round(240 * scale * textScale));
        int h = Math.Min(height, (int)Math.Round((gameMode ? 72 : 56) * scale * textScale));
        return new(x + (width - w) / 2, y + Math.Max(0, height - h - (int)Math.Round(48 * scale)), w, h);
    }
}
