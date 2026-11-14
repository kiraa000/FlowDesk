using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

// The crash log is a diagnostic file, never a copy of what the user was doing. These tests use
// throwaway folders and a fixed clock; they never touch %LOCALAPPDATA% or the real logs.
public sealed class CrashLogTests : IDisposable
{
    private const string WindowTitle = "Quarterly Layoffs - Notepad";
    private const string UserPath = @"D:\Private\private-plan.docx";
    private const string MessageSecret = "s3cr3t-token-value";
    private const string DataSecret = "data-secret-98765";
    private const string InnerOneSecret = "inner-one-secret";
    private const string InnerTwoSecret = "inner-two-secret";
    private const string OuterSecret = "outer-aggregate-secret";

    private readonly string root = Path.Combine(Path.GetTempPath(), "fd-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static DateTimeOffset Noon(int daysFromToday) =>
        new(DateTime.Today.AddDays(daysFromToday).AddHours(12));

    private static string LogFile(DateTimeOffset day) =>
        "flowdesk-" + day.ToString("yyyyMMdd") + ".log";

    [Fact]
    public void DefaultDirectory_IsTheLocalAppDataLogsFolder()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowDesk", "logs");

        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(CrashLog.DefaultDirectory), ignoreCase: true);
    }

    [Fact]
    public void TryWrite_CreatesOneDailyFileInTheGivenFolder()
    {
        var clock = Noon(0);
        var log = new CrashLog(root, () => clock);

        Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(log.DirectoryPath), ignoreCase: true);
        Assert.True(log.TryWrite(CrashSource.UiThread, new InvalidOperationException("boom")));

        string file = Path.Combine(root, LogFile(clock));
        Assert.True(File.Exists(file));
        Assert.Single(Directory.GetFiles(root, "flowdesk-*.log"));
    }

    [Fact]
    public void TryWrite_RecordsSourceTypeHResultAndMethodButNeverUserContent()
    {
        var clock = Noon(0);
        var log = new CrashLog(root, () => clock);

        Assert.True(log.TryWrite(CrashSource.AppDomain, ThrownUserException(), terminating: true));
        string text = File.ReadAllText(Path.Combine(root, LogFile(clock)));

        // Useful diagnostics: source, exception type, HResult and the throwing method.
        Assert.Contains(nameof(CrashSource.AppDomain), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(UnauthorizedAccessException), text, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            text.Contains("80070005", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("2147942405", StringComparison.Ordinal) ||
            text.Contains("-2147024891", StringComparison.Ordinal),
            "The log must keep the exception HResult, in hex or decimal form.");
        Assert.Contains(nameof(ThrownUserException), text, StringComparison.Ordinal);

        // Nothing that could identify a window, a file, a token or user data may be copied.
        Assert.DoesNotContain(WindowTitle, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(UserPath, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\Private", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-plan.docx", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(MessageSecret, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DataSecret, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryWrite_RecordsInnerAndAggregateTypesWithoutTheirMessages()
    {
        var clock = Noon(0);
        var log = new CrashLog(root, () => clock);

        Assert.True(log.TryWrite(CrashSource.UnobservedTask, ThrownAggregate()));
        string text = File.ReadAllText(Path.Combine(root, LogFile(clock)));

        Assert.Contains(nameof(AggregateException), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(ArgumentException), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(FormatException), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OuterSecret, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(InnerOneSecret, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(InnerTwoSecret, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryWrite_WithNullExceptionStillRecordsTheSource()
    {
        var clock = Noon(0);
        var log = new CrashLog(root, () => clock);

        Assert.True(log.TryWrite(CrashSource.Launch, null));
        string text = File.ReadAllText(Path.Combine(root, LogFile(clock)));
        Assert.Contains(nameof(CrashSource.Launch), text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryWrite_ReturnsFalseInsteadOfThrowingWhenTheFolderCannotBeWritten()
    {
        Directory.CreateDirectory(root);
        string blocker = Path.Combine(root, "not-a-folder");
        File.WriteAllText(blocker, "file");
        var log = new CrashLog(blocker, () => Noon(0));

        Assert.False(log.TryWrite(CrashSource.Launch, new InvalidOperationException("boom")));
    }

    [Fact]
    public void TryWrite_ReturnsFalseInsteadOfThrowingWhenTodaysFileIsReadOnly()
    {
        var clock = Noon(0);
        var log = new CrashLog(root, () => clock);
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, LogFile(clock));
        File.WriteAllText(file, "existing");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        try
        {
            Assert.False(log.TryWrite(CrashSource.Launch, new InvalidOperationException("boom")));
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void TenDailyWritesKeepTheSevenNewestStrictDatesAndLeaveUnrelatedFilesAlone()
    {
        string[] unrelated =
        [
            "notes.txt", "flowdesk-notes.log", "flowdesk-2026-01-01.log",
            "flowdesk-20261340.log", "other-20260101.log"
        ];
        Directory.CreateDirectory(root);
        foreach (string name in unrelated) File.WriteAllText(Path.Combine(root, name), "keep");

        var clock = Noon(-9);
        var log = new CrashLog(root, () => clock);
        for (int day = 0; day < 10; day++)
        {
            clock = Noon(day - 9);
            Assert.True(log.TryWrite(CrashSource.Launch, null));
        }

        var expected = Enumerable.Range(3, 7)
            .Select(day => LogFile(Noon(day - 9)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = Directory.GetFiles(root, "flowdesk-*.log")
            .Select(path => Path.GetFileName(path)!)
            .Where(name => expected.Contains(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(7, kept.Count);
        Assert.Equal(expected, kept);
        foreach (string name in unrelated) Assert.True(File.Exists(Path.Combine(root, name)), name);
    }

    [Fact]
    public void ConcurrentWritesKeepEveryRecord()
    {
        var clock = Noon(0);
        var log = new CrashLog(root, () => clock);
        Exception[] markers = [new MarkerAException(), new MarkerBException(), new MarkerCException(), new MarkerDException()];

        Parallel.For(0, 40, i =>
            Assert.True(log.TryWrite(i % 2 == 0 ? CrashSource.UiThread : CrashSource.UnobservedTask, markers[i % 4])));

        string text = File.ReadAllText(Path.Combine(root, LogFile(clock)));
        foreach (var marker in markers)
        {
            string name = marker.GetType().Name;
            int count = text.Split(name, StringSplitOptions.None).Length - 1;
            Assert.Equal(10, count);
        }
    }

    private static Exception ThrownUserException()
    {
        var exception = new UnauthorizedAccessException(
            $"Could not save {WindowTitle} from {UserPath} (token={MessageSecret})");
        exception.Data["apiKey"] = DataSecret;
        try { throw exception; } catch (Exception caught) { return caught; }
    }

    private static Exception ThrownAggregate()
    {
        var first = new ArgumentException("first: " + InnerOneSecret);
        var second = new FormatException("second: " + InnerTwoSecret);
        var aggregate = new AggregateException(OuterSecret, first, second);
        try { throw aggregate; } catch (Exception caught) { return caught; }
    }

    private sealed class MarkerAException : Exception { }
    private sealed class MarkerBException : Exception { }
    private sealed class MarkerCException : Exception { }
    private sealed class MarkerDException : Exception { }
}
