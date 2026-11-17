namespace FlowDesk.App.Services;

public sealed record PaletteNotice(string Message, bool IsCopied = false)
{
    public TimeSpan? DismissAfter => IsCopied ? TimeSpan.FromSeconds(5) : null;
    public static PaletteNotice Copied(bool pasteFallback) => new(
        pasteFallback ? "Copied. Press Ctrl+V to paste." : "Copied to clipboard.", IsCopied: true);
}

/// <summary>Expiry is checked on the UI queue, not the timer thread. Newer status always wins.</summary>
public sealed class StatusLifetime
{
    public long Generation { get; private set; }
    public void Invalidate() => Generation++;
    public async Task DismissAsync(long generation, Func<Task> delay, Action<Action> enqueue, Action dismiss)
    {
        await delay().ConfigureAwait(false);
        enqueue(() => { if (generation == Generation) dismiss(); });
    }
}
