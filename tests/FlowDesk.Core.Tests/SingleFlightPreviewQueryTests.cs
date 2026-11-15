using FlowDesk.Core.QuickLook;

namespace FlowDesk.Core.Tests;

public sealed class SingleFlightPreviewQueryTests
{
    [Fact] public async Task TimeoutKeepsLeaseUntilNativeCallbackActuallyReturns()
    {
        using var queries = new SingleFlightPreviewQuery();
        Action? queued = null;
        int scheduled = 0;
        bool Schedule(Action action) { queued = action; scheduled++; return true; }
        Assert.Null(await queries.RunAsync(() => "old", Schedule, TimeSpan.Zero, default));
        Assert.Null(await queries.RunAsync(() => "wrong", Schedule, TimeSpan.Zero, default));
        Assert.Equal(1, scheduled);
        queued!();
        Assert.Equal("new", await queries.RunAsync(() => "new", action => { action(); return true; }, TimeSpan.FromSeconds(1), default));
    }
    [Fact] public async Task CancelledQueuedWorkSkipsComButHoldsLeaseUntilDequeued()
    {
        using var queries = new SingleFlightPreviewQuery();
        using var cts = new CancellationTokenSource();
        Action? queued = null; bool executed = false;
        var pending = queries.RunAsync(() => { executed = true; return "x"; }, action => { queued = action; return true; }, TimeSpan.FromSeconds(10), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(await queries.RunAsync(() => "wrong", _ => throw new Exception("must not schedule"), TimeSpan.Zero, default));
        queued!(); Assert.False(executed);
        Assert.Equal("ok", await queries.RunAsync(() => "ok", action => { action(); return true; }, TimeSpan.FromSeconds(1), default));
    }
    [Fact] public async Task FailedSchedulingAndOperationRecover()
    {
        using var queries = new SingleFlightPreviewQuery();
        Assert.Null(await queries.RunAsync(() => "x", _ => false, TimeSpan.Zero, default));
        Assert.Null(await queries.RunAsync(() => "x", _ => throw new InvalidOperationException(), TimeSpan.Zero, default));
        Assert.Null(await queries.RunAsync<string>(() => throw new InvalidOperationException(), action => { action(); return true; }, TimeSpan.FromSeconds(1), default));
        Assert.Equal("ok", await queries.RunAsync(() => "ok", action => { action(); return true; }, TimeSpan.FromSeconds(1), default));
    }
    [Fact] public async Task DisposeSkipsQueuedWorkAndRejectsFutureRequests()
    {
        var queries = new SingleFlightPreviewQuery();
        Action? queued = null; bool executed = false;
        var pending = queries.RunAsync(() => { executed = true; return "x"; }, action => { queued = action; return true; }, TimeSpan.FromSeconds(1), default);
        queries.Dispose(); queued!(); Assert.Null(await pending); Assert.False(executed);
        Assert.Null(await queries.RunAsync(() => "x", _ => throw new Exception(), TimeSpan.Zero, default));
    }
}
