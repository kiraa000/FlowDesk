namespace FlowDesk.Core.HotCorners;

public readonly record struct HotCornerDecision(CornerLocation? Fired, HotCornerAction Action, string? Diagnostic);

/// <summary>Single worker owns this state. Blocked/enabled/configuration boundaries require leaving
/// the corner buffer first. Missing a trigger is safer than firing when a drag, lock or full-screen ends.</summary>
public sealed class HotCornerTrigger
{
    public const int DwellMilliseconds = 250;
    public const int RearmRadius = 24;
    public static bool ActionIsFresh(long firedAt, long now, long expectedContext = 0, long currentContext = 0) =>
        expectedContext == currentContext && now >= firedAt && now - firedAt <= DwellMilliseconds;
    private CornerLocation? pending;
    private CornerPoint? latched;
    private bool requireNeutral = true;
    private bool hasTick;
    private long lastTick;
    private long enteredAt, revision;
    public void Reset() { pending = null; latched = null; requireNeutral = true; hasTick = false; }

    public HotCornerDecision Tick(CornerPoint point, long mouseRevision, long now, IReadOnlyList<CornerBounds> monitors,
        HotCornerConfiguration configuration, string? blocked)
    {
        bool gap = hasTick && (now < lastTick || now - lastTick > DwellMilliseconds);
        lastTick = now; hasTick = true;
        if (gap) { pending = null; latched = null; requireNeutral = true; }
        if (latched is CornerPoint anchor)
        {
            if (HotCornerGeometry.Near(point, anchor, RearmRadius)) return default;
            latched = null;
        }
        if (requireNeutral)
        {
            if (monitors.SelectMany(HotCornerGeometry.Corners).Any(c => HotCornerGeometry.Near(point, c.Point, RearmRadius)))
                return new(null, HotCornerAction.None, gap ? "cancel: sample gap/resume" : null);
            requireNeutral = false;
        }
        var corner = HotCornerGeometry.At(point, monitors);
        string? reason = !configuration.Enabled ? "disabled" : blocked;
        if (corner is CornerLocation found && !HotCornerGeometry.Outer(found, monitors)) reason = "inner corner";
        if (reason is not null)
        {
            bool hadPending = pending is not null;
            pending = null;
            if (corner is not null) latched = point;
            return new(null, HotCornerAction.None, hadPending || corner is not null ? $"cancel: {reason}" : null);
        }
        if (corner is null || configuration.For(corner.Value.Corner) == HotCornerAction.None)
        {
            bool left = pending is not null; pending = null;
            return new(null, HotCornerAction.None, left ? "cancel: left early" : null);
        }
        if (pending != corner || mouseRevision != revision || now < enteredAt)
        {
            pending = corner; revision = mouseRevision; enteredAt = now;
            return new(null, HotCornerAction.None, $"enter: {corner.Value.Corner}");
        }
        if (now - enteredAt < DwellMilliseconds) return default;
        pending = null; latched = point;
        return new(corner, configuration.For(corner.Value.Corner), $"dwell fire: {corner.Value.Corner}");
    }
}
