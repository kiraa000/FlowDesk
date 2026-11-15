using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.QuickLook;

public enum QuickLookCommand { None, Toggle, Close, Open }
public readonly record struct QuickLookContext(bool Enabled, nint Foreground, string ForegroundClass,
    string FocusClass, bool UnderShellView, bool ImeComposing, ShortcutModifiers Modifiers, nint PreviewOwner);
public readonly record struct QuickLookKeyDecision(bool Swallow, QuickLookCommand Command);
public interface IQuickLookInput
{
    bool Enabled { get; }
    nint PreviewOwner { get; }
    void Post(QuickLookCommand command, nint owner);
}

/// <summary>Hook-thread state. Only an accepted down owns its repeats and matching up, even after
/// focus/settings change. An unaccepted down can never become a trigger on an auto-repeat.</summary>
public sealed class QuickLookTrigger
{
    private readonly HashSet<int> held = [];
    private readonly HashSet<int> swallowed = [];
    public static bool IsCandidate(int key) => key is 0x20 or 0x1B or 0x0D;

    public QuickLookKeyDecision Observe(int key, bool down, bool injected, QuickLookContext context)
    {
        if (!IsCandidate(key) || injected) return default;
        if (!down)
        {
            held.Remove(key);
            return new(swallowed.Remove(key), QuickLookCommand.None);
        }
        if (!held.Add(key)) return new(swallowed.Contains(key), QuickLookCommand.None);
        var m = context.Modifiers;
        if (!context.Enabled || m.Win || m.Alt || m.Shift || m.Control || context.ImeComposing ||
            context.Foreground == 0 || !context.UnderShellView || !IsItemView(context.ForegroundClass, context.FocusClass) ||
            context.ForegroundClass is not ("CabinetWClass" or "Progman" or "WorkerW")) return default;
        bool open = context.PreviewOwner != 0 && context.Foreground == context.PreviewOwner;
        QuickLookCommand command = key switch
        {
            0x20 => QuickLookCommand.Toggle,
            0x1B when open => QuickLookCommand.Close,
            0x0D when open => QuickLookCommand.Open,
            _ => QuickLookCommand.None
        };
        if (command == QuickLookCommand.None) return default;
        swallowed.Add(key);
        return new(true, command);
    }
    private static bool IsItemView(string foreground, string focus) => focus == "DirectUIHWND" ||
        // Windows' desktop uses the legacy icon view, not Explorer's DirectUI host.
        (foreground is "Progman" or "WorkerW" && focus == "SysListView32");
}
