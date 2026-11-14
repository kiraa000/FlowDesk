using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FlowDesk.App.Services;

public enum CrashSource { UiThread, UnobservedTask, AppDomain, Activation, Launch }

/// <summary>
/// Local, best-effort crash diagnostics. Deliberately omits exception messages/ToString/Data,
/// file/line information, activation arguments and window metadata: those can contain private data.
/// </summary>
public sealed class CrashLog(string? directory = null, Func<DateTimeOffset>? clock = null)
{
    private readonly object gate = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.Now);
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowDesk", "logs");
    public string DirectoryPath { get; } = directory ?? DefaultDirectory;

    public bool TryWrite(CrashSource source, Exception? exception, bool terminating = false)
    {
        try
        {
            lock (gate)
            {
                var at = now();
                var text = new StringBuilder();
                text.Append('[').Append(at.ToString("O", CultureInfo.InvariantCulture)).Append("] Source=")
                    .Append(source).Append(" Terminating=").Append(terminating)
                    .Append(" Thread=").Append(Environment.CurrentManagedThreadId).AppendLine();
                AppendExceptionTypesAndMethods(text, exception);
                text.AppendLine();
                Directory.CreateDirectory(DirectoryPath);
                string file = Path.Combine(DirectoryPath, $"flowdesk-{at.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
                File.AppendAllText(file, text.ToString(), new UTF8Encoding(false));
                RetainSevenDays();
                return true;
            }
        }
        catch (Exception)
        {
            // A failure in crash reporting must never recurse, hide the original exception or throw
            // from a finalizer/AppDomain handler. This is not a recovery or telemetry mechanism.
            return false;
        }
    }

    private static void AppendExceptionTypesAndMethods(StringBuilder text, Exception? error)
    {
        if (error is null) { text.AppendLine("Exception: (none)"); return; }
        var todo = new Queue<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        todo.Enqueue(error);
        while (todo.Count > 0 && seen.Count < 8)
        {
            var current = todo.Dequeue();
            if (!seen.Add(current)) continue;
            text.Append("Exception: ").Append(current.GetType().FullName)
                .Append(" HResult=0x").Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture)).AppendLine();
            // StackTrace.ToString would include source paths. Render only compiled type/method names.
            foreach (var frame in (new StackTrace(current, false).GetFrames() ?? []).Take(32))
            {
                var method = frame.GetMethod();
                if (method is not null) text.Append("  at ").Append(method.DeclaringType?.FullName)
                    .Append('.').Append(method.Name).AppendLine();
            }
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.Take(8)) todo.Enqueue(inner);
            }
            else if (current.InnerException is { } inner) todo.Enqueue(inner);
        }
    }

    private void RetainSevenDays()
    {
        var owned = Directory.EnumerateFiles(DirectoryPath, "flowdesk-????????.log")
            .Where(p => IsDailyLogName(Path.GetFileName(p)))
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal).ToList();
        foreach (string old in owned.Skip(7)) File.Delete(old);
    }

    private static bool IsDailyLogName(string name) => name.Length == 21
        && name.StartsWith("flowdesk-", StringComparison.Ordinal) && name.EndsWith(".log", StringComparison.Ordinal)
        && DateTime.TryParseExact(name.Substring(9, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
