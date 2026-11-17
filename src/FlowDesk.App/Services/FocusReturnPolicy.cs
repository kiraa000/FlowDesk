namespace FlowDesk.App.Services;

/// <summary>Prefer the invoker, then stop at the first usable control in its own card.</summary>
public static class FocusReturnPolicy
{
    public static bool TryRestore<T>(IEnumerable<T> candidates, Func<T, bool> tryFocus)
    {
        foreach (var candidate in candidates)
            if (tryFocus(candidate)) return true;
        return false;
    }
}
