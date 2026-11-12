namespace FlowDesk.Core.Hotkeys;

/// <summary>Schedules a single callback; disposing the handle cancels it if it has not fired yet.</summary>
public interface IOneShotScheduler
{
    IDisposable Schedule(TimeSpan delay, Action callback);
}

/// <summary>
/// Multi-press bookkeeping for directional focus. Each press of a direction restarts a 140 ms
/// one-shot timer; when it fires, the accumulated count and direction are reported once.
/// The clock/timer is injectable so tests never wait.
/// </summary>
public sealed class MultiPressQueue(IOneShotScheduler scheduler)
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(140);

    private readonly object _gate = new();
    private WindowDirection? _direction;
    private int _count;
    private IDisposable? _timer;
    private long _nextToken;
    private long _activeToken;

    /// <summary>Raised on the timer callback with the direction and how many times it was pressed.</summary>
    public event Action<WindowDirection, int>? Fired;

    public void Press(WindowDirection direction)
    {
        lock (_gate)
        {
            if (_direction != direction)
            {
                _direction = direction;
                _count = 0;
            }
            _count++;
            _timer?.Dispose();
            long token = ++_nextToken;
            _activeToken = token;
            _timer = scheduler.Schedule(Window, () => Fire(token));
        }
    }

    /// <summary>Drops a pending sequence (feature turned off, host stopping).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _direction = null;
            _count = 0;
            _activeToken = 0;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Fire(long token)
    {
        WindowDirection? direction;
        int count;
        lock (_gate)
        {
            if (_activeToken != token) return; // a newer press replaced this timer
            _activeToken = 0;
            direction = _direction;
            count = _count;
            _direction = null;
            _count = 0;
            _timer?.Dispose();
            _timer = null;
        }
        if (direction is { } value && count > 0) Fired?.Invoke(value, count);
    }
}
