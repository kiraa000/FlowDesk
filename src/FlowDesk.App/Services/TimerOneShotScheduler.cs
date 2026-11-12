using System.Threading;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Services;

/// <summary>One-shot timer for the multi-press window; disposing the handle cancels a pending tick.</summary>
public sealed class TimerOneShotScheduler : IOneShotScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var timer = new Timer(_ =>
        {
            try { callback(); } catch (Exception) { /* the host contains its own failures */ }
        }, null, delay, Timeout.InfiniteTimeSpan);
        return timer;
    }
}
