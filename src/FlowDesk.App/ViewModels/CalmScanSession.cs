namespace FlowDesk.App.ViewModels;

/// <summary>Page visits share an in-flight read; a failed read can retry. No persistence or writes here.</summary>
internal sealed class CalmScanSession
{
    private readonly SemaphoreSlim gate = new(1, 1);
    internal async Task EnsureAsync(Func<bool> hasScan, Func<Task> scan)
    {
        await gate.WaitAsync();
        try { if (!hasScan()) await scan(); }
        finally { gate.Release(); }
    }
}
