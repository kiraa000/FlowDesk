using FlowDesk.Core.QuickLook;

namespace FlowDesk.Core.Tests;

public sealed class ShellPreviewQueriesTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private sealed class SerialQueue
    {
        public Queue<Action> Items { get; } = new();
        public bool Schedule(Action action)
        {
            if (Items.Count == ShellPreviewQueries.QueueCapacity) return false;
            Items.Enqueue(action); return true;
        }
        public void RunNext() => Items.Dequeue()();
    }

    [Fact] public async Task ThumbnailIsNotDroppedWhenPollIsQueuedAndRunsBeforeLaterPolls()
    {
        var queue = new SerialQueue();
        using var queries = new ShellPreviewQueries(queue.Schedule);
        var order = new List<string>();
        var read = queries.ReadAsync(() => { order.Add("read"); return "selected"; }, Deadline, default);
        var thumbnail = queries.ThumbnailAsync(() => { order.Add("thumbnail"); return "pixels"; }, Deadline, default);
        Assert.Equal(2, queue.Items.Count); Assert.False(thumbnail.IsCompleted);
        queue.RunNext(); Assert.Equal("selected", await read);
        var nextPoll = queries.ReadAsync(() => { order.Add("next poll"); return "next"; }, Deadline, default);
        Assert.Equal(2, queue.Items.Count);
        queue.RunNext(); Assert.Equal("pixels", await thumbnail);
        queue.RunNext(); Assert.Equal("next", await nextPoll);
        Assert.Equal(new[] { "read", "thumbnail", "next poll" }, order);
    }

    [Fact] public async Task ThumbnailCanBeAdmittedWhileReadCallbackIsActuallyRunning()
    {
        var queue = new SerialQueue();
        using var queries = new ShellPreviewQueries(queue.Schedule);
        Task<string?>? thumbnail = null;
        var read = queries.ReadAsync(() =>
        {
            thumbnail = queries.ThumbnailAsync(() => "pixels", Deadline, default);
            Assert.False(thumbnail.IsCompleted); Assert.Single(queue.Items);
            return "selected";
        }, Deadline, default);
        queue.RunNext(); Assert.Equal("selected", await read);
        Assert.NotNull(thumbnail); queue.RunNext(); Assert.Equal("pixels", await thumbnail);
    }

    [Fact] public async Task TimeoutRetainsEachLeaseAndBoundsTotalNativeWorkToTwo()
    {
        var queue = new SerialQueue();
        using var queries = new ShellPreviewQueries(queue.Schedule);
        Assert.Null(await queries.ReadAsync(() => "read", TimeSpan.Zero, default));
        Assert.Null(await queries.ThumbnailAsync(() => "thumbnail", TimeSpan.Zero, default));
        for (int i = 0; i < 20; i++)
        {
            Assert.Null(await queries.ReadAsync(() => "extra", TimeSpan.Zero, default));
            Assert.Null(await queries.ThumbnailAsync(() => "extra", TimeSpan.Zero, default));
        }
        Assert.Equal(2, queue.Items.Count);
        queue.RunNext(); // only the read callback's finally frees the read lease
        var nextRead = queries.ReadAsync(() => "recovered", Deadline, default);
        Assert.Null(await queries.ThumbnailAsync(() => "still busy", TimeSpan.Zero, default));
        Assert.Equal(2, queue.Items.Count);
        queue.RunNext(); queue.RunNext(); Assert.Equal("recovered", await nextRead);
    }

    [Fact] public async Task CancellationOfQueuedThumbnailSkipsItsNativeCallWithoutFreeingLeaseEarly()
    {
        var queue = new SerialQueue();
        using var queries = new ShellPreviewQueries(queue.Schedule);
        using var cts = new CancellationTokenSource();
        bool executed = false;
        var read = queries.ReadAsync(() => "read", Deadline, default);
        var thumbnail = queries.ThumbnailAsync(() => { executed = true; return "pixels"; }, Deadline, cts.Token);
        cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => thumbnail);
        Assert.Null(await queries.ThumbnailAsync(() => "extra", TimeSpan.Zero, default));
        Assert.Equal(2, queue.Items.Count);
        queue.RunNext(); Assert.Equal("read", await read);
        queue.RunNext(); Assert.False(executed);
        var next = queries.ThumbnailAsync(() => "new pixels", Deadline, default);
        queue.RunNext(); Assert.Equal("new pixels", await next);
    }

    [Fact] public async Task ExceptionsAndDisposalDoNotLeakAdmissionOrRunQueuedNativeWork()
    {
        var queue = new SerialQueue();
        var queries = new ShellPreviewQueries(queue.Schedule);
        var read = queries.ReadAsync<string>(() => throw new InvalidOperationException(), Deadline, default);
        var thumbnail = queries.ThumbnailAsync(() => "pixels", Deadline, default);
        queue.RunNext(); Assert.Null(await read);
        bool executed = false;
        var nextRead = queries.ReadAsync(() => { executed = true; return "x"; }, Deadline, default);
        queries.Dispose(); queue.RunNext(); queue.RunNext();
        Assert.Null(await thumbnail); Assert.Null(await nextRead); Assert.False(executed);
        Assert.Null(await queries.ReadAsync(() => "x", Deadline, default));
        Assert.Null(await queries.ThumbnailAsync(() => "x", Deadline, default));
        Assert.Empty(queue.Items);
    }
}
