namespace FlowDesk.App.Services;

/// <summary>Desktop-free decision seam. Wait six seconds for Windows' AutoRestartShell before fallback.</summary>
internal static class ExplorerRestartWait
{
    internal static async Task<bool> WaitAsync(uint previousPid, Func<uint?> currentShellPid, Func<TimeSpan, Task> delay)
    {
        for (int i = 0; i <= 24; i++)
        {
            uint? pid = currentShellPid();
            if (pid is > 0 && pid != previousPid) return true;
            if (i < 24) await delay(TimeSpan.FromMilliseconds(250));
        }
        return false;
    }
}
