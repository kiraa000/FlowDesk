using System.Collections.Frozen;

namespace FlowDesk.Core.Hotkeys;

/// <summary>Immutable ID-based preferences. Labels/key text are not persisted; future key overrides
/// can share these stable IDs without changing the enabled/disabled representation.</summary>
public sealed class ShortcutPolicy
{
    private readonly FrozenSet<string> disabled;
    private static readonly FrozenSet<string> Known = HotkeyCatalog.All.Select(b => b.Id).ToFrozenSet(StringComparer.Ordinal);
    public static ShortcutPolicy AllOff { get; } = new(HotkeyCatalog.All.Select(b => b.Id));
    public ShortcutPolicy(IEnumerable<string>? ids) => disabled = (ids ?? []).Where(id => id is not null && Known.Contains(id)).ToFrozenSet(StringComparer.Ordinal);
    public bool Enabled(string id) => Known.Contains(id) && !disabled.Contains(id);
    public bool IsCurrent(ShortcutPolicy current) => ReferenceEquals(this, current);
    public bool CanExecute(ShortcutPolicy current, string id) => IsCurrent(current) && Enabled(id);
    public IReadOnlyList<string> DisabledIds => HotkeyCatalog.All.Where(b => disabled.Contains(b.Id)).Select(b => b.Id).ToArray();
    public bool CanRegister(HotkeyBinding binding, bool desktopsAvailable) => !binding.IsMouseWheel && Enabled(binding.Id) && (!binding.IsDesktop || desktopsAvailable);
    public ShortcutPolicy ForModules(bool windowShortcuts, bool palette, bool clipboard = false) => new(HotkeyCatalog.All
        .Where(b => !Enabled(b.Id) || (b.Id == HotkeyCatalog.PaletteId ? !palette : b.Id == HotkeyCatalog.ClipboardId ? !clipboard : !windowShortcuts)).Select(b => b.Id));
    public static string? WheelId(WheelGesture gesture) => gesture switch
    {
        WheelGesture.Resize => "resize-wheel", WheelGesture.Desktop => "desktop-wheel",
        WheelGesture.MoveDesktop => "desktop-move-wheel", WheelGesture.Search => "search-wheel", _ => null
    };
    public bool AllowsWheel(WheelGesture gesture, bool desktopsAvailable = true) => WheelId(gesture) is string id && Enabled(id) &&
        (desktopsAvailable || gesture is not (WheelGesture.Desktop or WheelGesture.MoveDesktop));
}

public enum ShortcutGroup { Desktops, Snap, Focus, WindowCommands, Tools, MouseWheel, CommandPalette }
public enum ShortcutStatus { Active, Off, Conflict, Unavailable }
public sealed record ShortcutGroupInfo(ShortcutGroup Group, string Title);
public sealed record ShortcutRegistrationSnapshot(IReadOnlySet<string> Registered, IReadOnlySet<string> Conflicts)
{
    public static ShortcutRegistrationSnapshot Empty { get; } = new(Array.Empty<string>().ToFrozenSet(), Array.Empty<string>().ToFrozenSet());
}
public sealed record ShortcutRuntime(bool Running, bool DesktopsAvailable, bool MouseHook, bool KeyboardHook, ShortcutRegistrationSnapshot Registration, bool Paused = false);

public static class ShortcutPresentation
{
    public static IReadOnlyList<ShortcutGroupInfo> Groups { get; } = Array.AsReadOnly(new[] {
        new ShortcutGroupInfo(ShortcutGroup.CommandPalette, "Command palette"),
        new ShortcutGroupInfo(ShortcutGroup.Desktops, "Desktops"), new(ShortcutGroup.Snap, "Snap"), new(ShortcutGroup.Focus, "Focus"),
        new(ShortcutGroup.WindowCommands, "Window commands"), new(ShortcutGroup.Tools, "Terminal, reload and pause"), new(ShortcutGroup.MouseWheel, "Mouse wheel") });
    public static ShortcutGroup Group(HotkeyBinding binding) => binding.Id is HotkeyCatalog.PaletteId or HotkeyCatalog.ClipboardId ? ShortcutGroup.CommandPalette : binding.IsMouseWheel ? ShortcutGroup.MouseWheel : binding.IsDesktop ? ShortcutGroup.Desktops :
        binding.Id.StartsWith("snap-", StringComparison.Ordinal) ? ShortcutGroup.Snap : binding.Id.StartsWith("focus-", StringComparison.Ordinal) ? ShortcutGroup.Focus :
        binding.Id is "terminal-open" or "engine-reload" or "pause" or HotkeyCatalog.PauseToggleId ? ShortcutGroup.Tools : ShortcutGroup.WindowCommands;
    public static bool Matches(HotkeyBinding binding, string? filter) => string.IsNullOrWhiteSpace(filter) ||
        binding.Label.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase) || binding.Shortcut.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    public static bool NeedsDesktops(HotkeyBinding binding) => binding.IsDesktop || binding.Id is "desktop-wheel" or "desktop-move-wheel";
    public static ShortcutStatus Status(HotkeyBinding binding, bool moduleEnabled, ShortcutPolicy policy, ShortcutRuntime runtime)
    {
        if (!moduleEnabled || !policy.Enabled(binding.Id)) return ShortcutStatus.Off;
        if (!runtime.Running || (NeedsDesktops(binding) && !runtime.DesktopsAvailable)) return ShortcutStatus.Unavailable;
        if (binding.IsMouseWheel) return runtime.MouseHook && ((binding.Modifiers & HotkeyCatalog.ModWin) == 0 || runtime.KeyboardHook)
            ? ShortcutStatus.Active : ShortcutStatus.Unavailable;
        if (runtime.Registration.Conflicts.Contains(binding.Id)) return ShortcutStatus.Conflict;
        return runtime.Registration.Registered.Contains(binding.Id) ? ShortcutStatus.Active : ShortcutStatus.Unavailable;
    }
    public static string StatusText(ShortcutStatus status) => status switch {
        ShortcutStatus.Active => "Active", ShortcutStatus.Off => "Off", ShortcutStatus.Conflict => "In use by another app", _ => "Not available" };
    public static bool ShowStatusDetail(ShortcutStatus status) => status != ShortcutStatus.Active;
    public static bool IsConflict(ShortcutStatus status) => status == ShortcutStatus.Conflict;
}

/// <summary>Reusable optional-tab rule; an unlisted page is always visible.</summary>
public static class OptionalNavigation
{
    public static bool Visible<T>(T page, IReadOnlyDictionary<T, bool> optionalPages) where T : notnull => !optionalPages.TryGetValue(page, out bool visible) || visible;
    public static T Resolve<T>(T requested, T fallback, IReadOnlyDictionary<T, bool> optionalPages) where T : notnull => Visible(requested, optionalPages) ? requested : fallback;
}
