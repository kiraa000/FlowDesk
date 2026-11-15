using System.Diagnostics;

namespace FlowDesk.App.Services;

internal static class QuickLookDiagnostics
{
#if DEBUG
    private static readonly object Gate = new();
#endif
    [Conditional("DEBUG")]
    public static void Write(string detail)
    {
#if DEBUG
        string? path = Environment.GetEnvironmentVariable("FLOWDESK_QUICKLOOK_DIAGNOSTICS_LOG");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
        string line = $"{DateTimeOffset.UtcNow:O} {detail}{Environment.NewLine}";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { lock (Gate) File.AppendAllText(path, line); }
            catch (Exception) { /* optional diagnostics never affect previews */ }
        });
#endif
    }
}
