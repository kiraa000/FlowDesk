using System.Runtime.InteropServices;

namespace FlowDesk.App.Services;

/// <summary>A bounded caller wait, not unsafe thread termination. A timed-out service is quarantined
/// for this process; a hung COM call may finish later, but cannot issue subsequent writes.</summary>
internal sealed class ContextNativeCall(Action<Exception> log)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile bool timedOut;
    public async Task<T> RunAsync<T>(Func<CancellationToken, T> action)
    {
        if (timedOut) throw new TimeoutException();
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false)) throw new TimeoutException();
        if (timedOut) { gate.Release(); throw new TimeoutException(); }
        var cancel = new CancellationTokenSource();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            int hr = CoInitializeEx(0, 0); // COINIT_MULTITHREADED
            try
            {
                Marshal.ThrowExceptionForHR(hr);
                cancel.Token.ThrowIfCancellationRequested();
                completion.TrySetResult(action(cancel.Token));
            }
            catch (Exception ex) { log(ex); completion.TrySetException(ex); }
            finally
            {
                if (hr >= 0) CoUninitialize();
                gate.Release();
            }
        }) { IsBackground = true, Name = "FlowDesk context COM" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            timedOut = true; cancel.Cancel();
            // Observe a late COM fault without waiting for it or leaking an unobserved exception.
            _ = completion.Task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }
    public static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
