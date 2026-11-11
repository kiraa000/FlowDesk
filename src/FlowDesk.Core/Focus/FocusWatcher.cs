using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Focus;

public enum BlockAction { Warned, Closed, Minimized }

/// <summary>Raised when a blocked app showed a window. The UI shows a warning / "Allow 5 min" from this.</summary>
public sealed record AppBlockedEvent(WindowInfo Window, FocusAppRule? Rule, string AppName, BlockAction Action, bool CanAllowTemporarily);

public sealed record FocusSessionState(DateTimeOffset StartedAt, DateTimeOffset EndsAt, FocusStrictness Strictness, bool Paused);

/// <summary>
/// Enforces a focus session by polling the open app windows. Windows are closed politely
/// (WM_CLOSE, so apps can save); a window that ignores it is minimized and closed again on the
/// next check. FlowDesk never kills processes. System shell windows and FlowDesk itself are exempt.
/// Thread-safe: the timer runs on the thread pool; events are raised there too (marshal to the UI thread).
/// </summary>
public sealed class FocusWatcher : IDisposable
{
    public static readonly TimeSpan TemporaryAllowance = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> SystemExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "applicationframehost", "searchhost", "searchapp", "startmenuexperiencehost",
        "shellexperiencehost", "textinputhost", "lockapp", "taskmgr", "systemsettings", "securityhealthsystray",
        "sihost", "ctfmon", "dwm", "winlogon", "consent", "credentialuibroker", "msedgewebview2", "openwith",
        "pickerhost", "smartscreen", "useroobebroker", "werfault",
    };

    private readonly IWindowSystem _windows;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<long, DateTimeOffset> _handled = new();   // window -> when we acted on it
    private readonly Dictionary<string, DateTimeOffset> _allowedUntil = new(); // app key -> temporary allowance end
    private ITimer? _timer;
    private FocusSettings? _settings;
    private FocusSessionState? _state;
    private TimeSpan _remainingWhenPaused;

    public FocusWatcher(IWindowSystem windows, TimeProvider? time = null)
    {
        _windows = windows;
        _time = time ?? TimeProvider.System;
    }

    public event EventHandler<AppBlockedEvent>? AppBlocked;
    /// <summary>Raised once when the session's time is up (watcher stops itself first).</summary>
    public event EventHandler? SessionEnded;

    public FocusSessionState? State { get { lock (_gate) return _state; } }

    public TimeSpan Remaining
    {
        get
        {
            lock (_gate)
            {
                if (_state is null) return TimeSpan.Zero;
                if (_state.Paused) return _remainingWhenPaused;
                var r = _state.EndsAt - _time.GetUtcNow();
                return r > TimeSpan.Zero ? r : TimeSpan.Zero;
            }
        }
    }

    public void Start(FocusSettings settings, TimeSpan? pollInterval = null)
    {
        lock (_gate)
        {
            _settings = Copy(settings);
            var now = _time.GetUtcNow();
            _state = new FocusSessionState(now, now + TimeSpan.FromMinutes(settings.DurationMinutes), settings.Strictness, false);
            _handled.Clear();
            _allowedUntil.Clear();
            _timer?.Dispose();
            var every = pollInterval ?? TimeSpan.FromMilliseconds(750);
            _timer = _time.CreateTimer(_ => Tick(), null, TimeSpan.Zero, every);
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_state is null || _state.Paused || _settings!.Strictness == FocusStrictness.Strict) return; // Strict can't be paused
            _remainingWhenPaused = _state.EndsAt - _time.GetUtcNow();
            _state = _state with { Paused = true };
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_state is not { Paused: true }) return;
            _state = _state with { Paused = false, EndsAt = _time.GetUtcNow() + _remainingWhenPaused };
        }
    }

    /// <summary>Ends the session early. Returns false in Strict mode unless <paramref name="force"/> (the UI asks twice).</summary>
    public bool Stop(bool force = false)
    {
        lock (_gate)
        {
            if (_state is null) return true;
            if (_settings!.Strictness == FocusStrictness.Strict && !force) return false;
            StopLocked();
            return true;
        }
    }

    /// <summary>"Allow 5 min" (Normal mode only). Returns false when not allowed.</summary>
    public bool AllowTemporarily(WindowInfo window)
    {
        lock (_gate)
        {
            if (_state is null || _settings!.Strictness != FocusStrictness.Normal) return false;
            _allowedUntil[window.AppKey] = _time.GetUtcNow() + TemporaryAllowance;
            return true;
        }
    }

    /// <summary>Runs one check now (also called by the timer). Public so tests and a foreground hook can trigger it.</summary>
    public void Tick()
    {
        List<AppBlockedEvent> raised = [];
        bool ended = false;
        lock (_gate)
        {
            if (_state is null || _settings is null) return;
            var now = _time.GetUtcNow();
            if (!_state.Paused && now >= _state.EndsAt)
            {
                StopLocked();
                ended = true;
            }
            else if (!_state.Paused)
            {
                var open = _windows.GetAppWindows();
                var openHandles = open.Select(w => w.Handle).ToHashSet();
                foreach (var h in _handled.Keys.Where(h => !openHandles.Contains(h)).ToList()) _handled.Remove(h);
                foreach (var k in _allowedUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList()) _allowedUntil.Remove(k);

                foreach (var w in open)
                {
                    if (!IsBlocked(w, _settings, out var rule)) continue;
                    if (_allowedUntil.ContainsKey(w.AppKey)) continue;
                    var e = Act(w, rule, now);
                    if (e is not null) raised.Add(e);
                }
            }
        }
        foreach (var e in raised) AppBlocked?.Invoke(this, e);
        if (ended) SessionEnded?.Invoke(this, EventArgs.Empty);
    }

    private AppBlockedEvent? Act(WindowInfo w, FocusAppRule? rule, DateTimeOffset now)
    {
        string name = rule?.Name is { Length: > 0 } n ? n : (w.ExeName.Length > 0 ? w.ExeName : w.Title);
        var strict = _settings!.Strictness;
        bool seen = _handled.TryGetValue(w.Handle, out var at);

        if (strict == FocusStrictness.Gentle)
        {
            if (seen) return null; // warn once per window
            _handled[w.Handle] = now;
            return new AppBlockedEvent(w, rule, name, BlockAction.Warned, false);
        }

        if (!seen)
        {
            _windows.Close(w.Handle);
            _handled[w.Handle] = now;
            return new AppBlockedEvent(w, rule, name, BlockAction.Closed, strict == FocusStrictness.Normal);
        }
        // Still open 2 s after WM_CLOSE (e.g. a "save changes?" prompt or an app that ignores it): tuck it away, retry.
        if (now - at >= TimeSpan.FromSeconds(2))
        {
            if (w.Show != WindowShow.Minimized) _windows.Minimize(w.Handle);
            _windows.Close(w.Handle);
            _handled[w.Handle] = now;
        }
        return null;
    }

    /// <summary>Pure rule check, exposed for tests and for the UI's "would this be blocked?" hints.</summary>
    public bool IsBlocked(WindowInfo w, FocusSettings s, out FocusAppRule? rule)
    {
        rule = null;
        if (w.ProcessId == _windows.OwnProcessId || w.Unresolved) return false;
        if (string.IsNullOrEmpty(w.ExePath) && string.IsNullOrEmpty(w.Aumid)) return false; // can't identify -> never touch
        if (SystemExes.Contains(w.ExeName) && string.IsNullOrEmpty(w.Aumid)) return false;
        if (s.Allowed.Any(r => r.Matches(w.ExePath, w.Aumid))) return false;
        rule = s.Blocked.FirstOrDefault(r => r.Matches(w.ExePath, w.Aumid));
        return rule is not null || s.BlockAllExceptAllowed;
    }

    private void StopLocked()
    {
        _timer?.Dispose();
        _timer = null;
        _state = null;
        _handled.Clear();
        _allowedUntil.Clear();
    }

    private static FocusSettings Copy(FocusSettings s) => new()
    {
        DurationMinutes = s.DurationMinutes, Strictness = s.Strictness, BlockAllExceptAllowed = s.BlockAllExceptAllowed,
        Blocked = [.. s.Blocked], Allowed = [.. s.Allowed],
    };

    public void Dispose()
    {
        lock (_gate) StopLocked();
    }
}
