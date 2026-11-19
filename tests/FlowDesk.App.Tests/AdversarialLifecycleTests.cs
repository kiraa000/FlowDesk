using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class AdversarialLifecycleTests
{
    [Fact]
    public async Task Failed_dnd_probe_retries_after_backoff_and_caches_success()
    {
        int probes = 0;
        var now = TimeSpan.Zero;
        var service = new DoNotDisturbService(() => Task.FromResult(++probes > 1), () => now);
        Assert.False(await service.ProbeAsync());
        now = TimeSpan.FromSeconds(4);
        Assert.False(await service.ProbeAsync());
        Assert.Equal(1, probes);
        now = TimeSpan.FromSeconds(5);
        Assert.True(await service.ProbeAsync());
        now = TimeSpan.FromDays(1);
        Assert.True(await service.ProbeAsync());
        Assert.Equal(2, probes);
    }

    [Fact]
    public async Task Concurrent_probe_callers_share_inflight_retry()
    {
        int probes = 0;
        var now = TimeSpan.Zero;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new DoNotDisturbService(() => { probes++; return pending.Task; }, () => now);
        var first = service.ProbeAsync();
        var second = service.ProbeAsync();
        Assert.Same(first, second);
        now = TimeSpan.FromSeconds(100);
        pending.SetResult(false);
        Assert.False(await first);
        Assert.False(await service.ProbeAsync()); // backoff starts on completion, not probe start
        Assert.Equal(1, probes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_dispatch_settles_both_autosave_call_shapes(bool asynchronous)
    {
        bool Reject(Action _) => false;
        var task = asynchronous ? UiDispatchTask.RunAsync(Reject, () => Task.FromResult(42)) : UiDispatchTask.Run(Reject, () => 42);
        Assert.True(task.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task Dispatch_returns_async_results_and_observes_work_exceptions()
    {
        bool Dispatch(Action action) { action(); return true; }
        Assert.Equal(42, await UiDispatchTask.RunAsync(Dispatch, () => Task.FromResult(42)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => UiDispatchTask.Run<int>(Dispatch, () => throw new InvalidOperationException()));
    }
}
