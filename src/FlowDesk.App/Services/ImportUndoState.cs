namespace FlowDesk.App.Services;

/// <summary>Only a completed import may arm Undo; the first subsequent save signal expires it.</summary>
internal sealed class ImportUndoState<T> where T : class
{
    private T? current;
    public event Action? Invalidated;
    public void Arm(T value) => current = value;
    public bool IsCurrent(T value) => ReferenceEquals(current, value);
    public void Invalidate()
    {
        if (current is null) return;
        current = null;
        Invalidated?.Invoke();
    }
}
