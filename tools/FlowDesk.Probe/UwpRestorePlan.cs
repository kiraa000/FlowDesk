// Pure selection rules for Probe uwp-restore. No title text is ever involved: the only identities are the
// ApplicationFrameWindow host and the child window's process id (the packaged app). Linked into the core
// test project so "exactly one frame hosts this exact app pid" is testable without a desktop.
namespace FlowDesk.Probe;

/// <summary>One top-level ApplicationFrameWindow and one child hosted by a different process.</summary>
public readonly record struct FrameChild(long Frame, int FramePid, long Child, int ChildPid, string ChildClass);

/// <summary>The single frame that hosts the requested packaged-app process.</summary>
public sealed record UwpFrameChoice(long Frame, int FramePid, long Child, int ChildPid, string ChildClass);

public static class UwpRestorePlan
{
    /// <summary>
    /// Chooses the ONE frame whose child process id is exactly <paramref name="appPid"/>.
    /// Refuses a zero/missing child, a virtual-desktop host as the app, and more than one hosting frame.
    /// </summary>
    public static bool TryChoose(int appPid, IReadOnlyList<FrameChild> candidates,
        out UwpFrameChoice choice, out string? error)
    {
        choice = null!;
        error = null;
        if (appPid <= 0)
        {
            error = "the packaged app process id must be a positive integer";
            return false;
        }

        // A frame's own pid is ApplicationFrameHost; only a child with a different pid is the packaged app.
        var matching = candidates.Where(c => c.ChildPid == appPid && c.FramePid != appPid).ToList();
        int frames = matching.Select(c => c.Frame).Distinct().Count();
        if (frames == 0)
        {
            error = $"no ApplicationFrameWindow child has pid {appPid}; refusing to guess by title or app name";
            return false;
        }
        if (frames > 1)
        {
            error = $"pid {appPid} appears under {frames} frames; refusing an ambiguous frame";
            return false;
        }

        // Same frame may expose more than one child boundary; the frame identity is what matters.
        var pick = matching[0];
        choice = new UwpFrameChoice(pick.Frame, pick.FramePid, pick.Child, pick.ChildPid, pick.ChildClass);
        return true;
    }
}
