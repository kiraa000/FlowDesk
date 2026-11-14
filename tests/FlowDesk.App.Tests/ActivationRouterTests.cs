using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

// The activation router is pure scheduling: a fake dispatcher queue proves whether a second launch
// (or a tray-hidden BringForward) runs once, while a real window is never created.
public sealed class ActivationRouterTests
{
    [Fact]
    public void RequestsBeforeTargetCoalesceAndRunOnceWhenTheTargetArrives()
    {
        var queue = new FakeQueue();
        using var router = new ActivationRouter(queue.TryEnqueue);
        var target = new Counter();

        router.Request();
        router.Request();
        router.Request();
        Assert.Equal(0, target.Calls);

        router.SetTarget(target.Invoke);
        queue.Drain();

        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public void QueuedDrainUsesTheLatestAction()
    {
        var queue = new FakeQueue();
        using var router = new ActivationRouter(queue.TryEnqueue);
        var first = new Counter();
        var second = new Counter();

        router.SetTarget(first.Invoke);
        router.Request();
        router.SetTarget(second.Invoke);
        queue.Drain();

        Assert.Equal(0, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public void RepeatedLaunchAndHiddenTrayRequestsRunTheActionOncePerBurst()
    {
        var queue = new FakeQueue();
        using var router = new ActivationRouter(queue.TryEnqueue);
        var target = new Counter();
        router.SetTarget(target.Invoke);

        for (int i = 0; i < 5; i++) router.Request();
        Assert.Equal(0, target.Calls);
        queue.Drain();
        Assert.Equal(1, target.Calls);

        router.Request();
        router.Request();
        queue.Drain();
        Assert.Equal(2, target.Calls);
    }

    [Fact]
    public void OneBringForwardActionServesHiddenMinimizedAndTrayRow()
    {
        var queue = new FakeQueue();
        using var router = new ActivationRouter(queue.TryEnqueue);
        var window = new FakeWindow();
        router.SetTarget(window.BringForward);

        foreach (var state in new[] { WindowState.Hidden, WindowState.Minimized, WindowState.Shown })
        {
            window.State = state;
            router.Request();
            queue.Drain();
            Assert.Equal(WindowState.Shown, window.State);
        }

        window.TrayRow.Invoke();
        Assert.Equal(4, window.BringForwardCalls);
        Assert.Equal(WindowState.Shown, window.State);
    }

    [Fact]
    public void FailedEnqueueKeepsTheRequestUntilTheNextRequest()
    {
        var queue = new FakeQueue { Allow = false };
        using var router = new ActivationRouter(queue.TryEnqueue);
        var target = new Counter();
        router.SetTarget(target.Invoke);

        // The first request really reaches the router while the dispatcher refuses callbacks.
        router.Request();
        queue.Drain();
        Assert.Equal(0, target.Calls);

        queue.Allow = true;
        router.Request();
        queue.Drain();
        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public void FailedEnqueueKeepsTheRequestUntilSetTarget()
    {
        var queue = new FakeQueue { Allow = false };
        using var router = new ActivationRouter(queue.TryEnqueue);
        var target = new Counter();
        router.Request();

        queue.Drain();
        Assert.Equal(0, target.Calls);

        queue.Allow = true;
        router.SetTarget(target.Invoke);
        queue.Drain();
        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public void ThrowingEnqueueKeepsTheRequestPendingForARetry()
    {
        var queue = new FakeQueue { Throw = true };
        using var router = new ActivationRouter(queue.TryEnqueue);
        var target = new Counter();
        router.SetTarget(target.Invoke);

        // A dispatcher that throws is not treated as delivered; the next schedule retries.
        Assert.Throws<InvalidOperationException>(() => router.Request());

        queue.Throw = false;
        router.Request();
        queue.Drain();
        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public void DisposalDropsQueuedCallbacks()
    {
        var queue = new FakeQueue();
        var router = new ActivationRouter(queue.TryEnqueue);
        var target = new Counter();
        router.SetTarget(target.Invoke);
        router.Request();

        router.Dispose();
        queue.Drain();

        Assert.Equal(0, target.Calls);
        router.Request();
        Assert.Equal(0, target.Calls);
    }

    [Fact]
    public void RequestsFromManyThreadsStillRunTheActionOnce()
    {
        var queue = new FakeQueue();
        using var router = new ActivationRouter(queue.TryEnqueue);
        var target = new Counter();
        router.SetTarget(target.Invoke);

        Parallel.For(0, 64, _ => router.Request());
        queue.Drain();

        Assert.Equal(1, target.Calls);
    }

    private sealed class Counter
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public void Invoke() => Interlocked.Increment(ref calls);
    }

    private enum WindowState { Hidden, Minimized, Shown }

    // Contract-only fake: the tray row and the locked activation path call the same counter action.
    private sealed class FakeWindow
    {
        private int calls;
        public WindowState State { get; set; } = WindowState.Hidden;
        public int BringForwardCalls => Volatile.Read(ref calls);
        public Action TrayRow => BringForward;
        public void BringForward()
        {
            Interlocked.Increment(ref calls);
            State = WindowState.Shown;
        }
    }

    private sealed class FakeQueue
    {
        private readonly object gate = new();
        private readonly List<Action> pending = [];
        private bool allow = true;
        private bool thrown;

        public bool Allow
        {
            get { lock (gate) return allow; }
            set { lock (gate) allow = value; }
        }

        public bool Throw
        {
            get { lock (gate) return thrown; }
            set { lock (gate) thrown = value; }
        }

        public bool TryEnqueue(Action action)
        {
            lock (gate)
            {
                if (thrown) throw new InvalidOperationException("dispatcher is shutting down");
                if (!allow) return false;
                pending.Add(action);
                return true;
            }
        }

        public void Drain()
        {
            Action[] run;
            lock (gate)
            {
                run = [.. pending];
                pending.Clear();
            }
            foreach (var action in run) action();
        }
    }
}
