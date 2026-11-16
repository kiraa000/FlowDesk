namespace FlowDesk.Core.HotCorners;

public interface IHotCornerInput
{
    void Observe(int x, int y, int message, uint mouseData);
    void SetHookAvailable(bool available);
    void SetSessionBlocked(bool blocked);
}
public readonly record struct HotCornerMouseSnapshot(CornerPoint Point, uint Buttons, long Revision,
    bool HookAvailable, bool SessionBlocked, bool HasPosition, long BoundaryRevision = 0);

/// <summary>Hook only records a tiny snapshot. Revision retains leave-and-return/button transitions
/// between worker polls, so short excursions cannot count as continuous dwell.</summary>
public sealed class HotCornerMouseState : IHotCornerInput
{
    private readonly object gate = new();
    private HotCornerMouseSnapshot state;
    public HotCornerMouseSnapshot Read() { lock (gate) return state; }
    public void SetHookAvailable(bool available)
    {
        lock (gate) state = state with { HookAvailable = available, HasPosition = false, Buttons = 0,
            Revision = state.Revision + 1, BoundaryRevision = state.BoundaryRevision + 1 };
    }
    public void SetSessionBlocked(bool blocked)
    {
        lock (gate) state = state with { SessionBlocked = blocked, HasPosition = false, Buttons = 0,
            Revision = state.Revision + 1, BoundaryRevision = state.BoundaryRevision + 1 };
    }
    public void Observe(int x, int y, int message, uint mouseData)
    {
        lock (gate)
        {
            uint bit = message switch { 0x201 or 0x202 => 1u, 0x204 or 0x205 => 2u, 0x207 or 0x208 => 4u,
                0x20B or 0x20C => (mouseData >> 16) == 1 ? 8u : 16u, _ => 0u };
            bool down = message is 0x201 or 0x204 or 0x207 or 0x20B;
            var point = new CornerPoint(x, y);
            long revision = state.Revision + (point != state.Point || bit != 0 || !state.HasPosition ? 1 : 0);
            state = state with { Point = point, Buttons = bit == 0 ? state.Buttons : down ? state.Buttons | bit : state.Buttons & ~bit,
                Revision = revision, HasPosition = true };
        }
    }
}
