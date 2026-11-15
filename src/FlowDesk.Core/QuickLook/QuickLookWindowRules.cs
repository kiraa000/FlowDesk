namespace FlowDesk.Core.QuickLook;

public readonly record struct ShellTabCandidate(nint Owner, nint Tab);
public static class QuickLookWindowRules
{
    // HKL high bits are opaque. PRIMARYLANGID(LANGID) is the low ten bits, NOT ImmIsIME.
    public static bool BypassSpaceForLayout(nint layout) => ((long)layout & 0x3ff) is 4 or 17 or 18;

    /// <summary>Never fall back to a background tab when an active tab HWND is known. With legacy
    /// Explorer (no tab HWND), only one matching ShellWindows entry is unambiguous.</summary>
    public static int SelectTab(nint owner, nint activeTab, IReadOnlyList<ShellTabCandidate> candidates)
    {
        int result = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate.Owner != owner || (activeTab != 0 && candidate.Tab != activeTab)) continue;
            if (result >= 0) return -1;
            result = i;
        }
        return result;
    }

    public static (nint InsertAfter, uint Flags) ZOrder(bool show) =>
        (show ? -1 : -2, 0x0001u | 0x0002u | 0x0010u | (show ? 0x0040u : 0u)); // NOMOVE | NOSIZE | NOACTIVATE | SHOWWINDOW
}
