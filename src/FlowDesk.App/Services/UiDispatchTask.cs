namespace FlowDesk.App.Services;

/// <summary>Awaited UI dispatch must settle even when enqueue rejects during shutdown.</summary>
internal static class UiDispatchTask
{
    internal static Task<T> Run<T>(Func<Action, bool> enqueue, Func<T> work) =>
        RunAsync(enqueue, () => Task.FromResult(work()));

    internal static Task<T> RunAsync<T>(Func<Action, bool> enqueue, Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!enqueue(async () =>
            {
                try { completion.TrySetResult(await work()); }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            })) completion.TrySetCanceled();
        }
        catch (OperationCanceledException) { completion.TrySetCanceled(); }
        catch (Exception ex) { completion.TrySetException(ex); }
        return completion.Task;
    }
}
