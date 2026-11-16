namespace FlowDesk.Core.Palette;

public sealed record PaletteActionState(bool Busy, bool FocusRunning, bool CanStartFocus, bool ShortcutsEnabled, bool HasTarget);
public static class PaletteActionRules
{
    public static bool Applies(string action, PaletteActionState state) => !state.Busy && (action switch
    {
        "start-focus" => !state.FocusRunning && state.CanStartFocus,
        "stop-focus" => state.FocusRunning,
        "pause-shortcuts" => state.ShortcutsEnabled,
        "layout" => state.HasTarget,
        _ => true
    });
}
