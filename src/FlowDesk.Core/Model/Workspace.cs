// Persisted data: workspaces, their items and where each item's window goes.
// Plain classes so System.Text.Json can round-trip them; no Win32 types here.
namespace FlowDesk.Core.Model;

/// <summary>What a workspace item opens.</summary>
public enum ItemKind { App, Website, File, Folder }

/// <summary>How a window was shown when it was saved.</summary>
public enum WindowShow { Normal, Maximized, Minimized }

/// <summary>A rectangle in physical screen pixels (virtual-screen coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public PixelRect Intersect(PixelRect o)
    {
        int x = Math.Max(X, o.X), y = Math.Max(Y, o.Y);
        int r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r > x && b > y ? new PixelRect(x, y, r - x, b - y) : default;
    }

    public static PixelRect FromLtrb(int l, int t, int r, int b) => new(l, t, r - l, b - t);
}

/// <summary>
/// A rectangle as FRACTIONS (0..1) of a monitor's work area. Layout presets
/// produce these; they are turned into pixels for a concrete monitor.
/// </summary>
public readonly record struct FractionRect(double X, double Y, double W, double H);

/// <summary>Identifies a program that can be launched again.</summary>
public sealed class AppRef
{
    /// <summary>Full path of the executable (empty for packaged apps that only have an AUMID).</summary>
    public string ExePath { get; set; } = "";
    /// <summary>AppUserModelID for packaged (Store/MSIX) apps; launched via shell:AppsFolder.</summary>
    public string? Aumid { get; set; }
    /// <summary>Command-line arguments to pass on launch (captured, user-editable).</summary>
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }

    /// <summary>Stable identity used for matching windows to this app.</summary>
    public string Key => AppKeys.For(ExePath, Aumid);

    public AppRef Clone() => (AppRef)MemberwiseClone();
}

public static class AppKeys
{
    /// <summary>AUMID when present (packaged apps share host exes), else the lower-cased exe path.</summary>
    public static string For(string? exePath, string? aumid) =>
        !string.IsNullOrEmpty(aumid) ? "aumid:" + aumid.ToLowerInvariant()
        : "exe:" + (exePath ?? "").Trim().ToLowerInvariant();

    /// <summary>File name of the exe without extension, lower case ("chrome").</summary>
    public static string ExeName(string? exePath) =>
        string.IsNullOrEmpty(exePath) ? "" : Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
}

/// <summary>Where and how an item's window should appear.</summary>
public sealed class SavedWindow
{
    /// <summary>Outer window rect (GetWindowRect, includes invisible resize borders) in its normal state.</summary>
    public PixelRect Rect { get; set; }
    public WindowShow Show { get; set; }
    /// <summary>Monitor device name at save time, e.g. \\.\DISPLAY1.</summary>
    public string MonitorDevice { get; set; } = "";
    /// <summary>Monitor bounds and work area at save time, to detect a changed display setup.</summary>
    public PixelRect MonitorBounds { get; set; }
    public PixelRect MonitorWorkArea { get; set; }
    /// <summary>Window title at save time; helps tell apart several windows of one app.</summary>
    public string Title { get; set; } = "";
    public string ClassName { get; set; } = "";
    /// <summary>0 = topmost of the workspace.</summary>
    public int ZOrder { get; set; }

    public SavedWindow Clone() => (SavedWindow)MemberwiseClone();
}

/// <summary>One thing a workspace opens.</summary>
public sealed class WorkspaceItem
{
    public string Id { get; set; } = NewId();
    public ItemKind Kind { get; set; }
    /// <summary>Display name ("Visual Studio Code", "Report.docx").</summary>
    public string Name { get; set; } = "";
    /// <summary>App items: the program. File items: optional program to open the file with.</summary>
    public AppRef? App { get; set; }
    /// <summary>File or folder path (File/Folder items).</summary>
    public string? Path { get; set; }
    /// <summary>Website items: URLs opened together in one new browser window.</summary>
    public List<string> Urls { get; set; } = [];
    /// <summary>Saved window position; null = let the app decide.</summary>
    public SavedWindow? Window { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public WorkspaceItem Clone() => new()
    {
        Id = Id, Kind = Kind, Name = Name, App = App?.Clone(), Path = Path,
        Urls = [.. Urls], Window = Window?.Clone(),
    };
}

/// <summary>A named working environment.</summary>
public sealed class Workspace
{
    public string Id { get; set; } = WorkspaceItem.NewId();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Segoe Fluent Icons glyph code, e.g. "E7C3".</summary>
    public string Glyph { get; set; } = "E8A1";
    /// <summary>Tag colour key chosen by the user (UI maps it to a brush + label).</summary>
    public string Color { get; set; } = "blue";
    public List<WorkspaceItem> Items { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastUsed { get; set; }
    public DateTimeOffset? LayoutSavedAt { get; set; }

    public Workspace Clone() => new()
    {
        Id = Id, Name = Name, Description = Description, Glyph = Glyph, Color = Color,
        Items = Items.Select(i => i.Clone()).ToList(),
        CreatedAt = CreatedAt, LastUsed = LastUsed, LayoutSavedAt = LayoutSavedAt,
    };
}
