using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FlowDesk.App.Services;
using Xunit.Abstractions;

namespace FlowDesk.App.Tests;

// Startup cleanup is filesystem-only but process-wide (TMP/TEMP scoping), so it never runs in
// parallel with the other update tests. No real network, installer or FlowDesk process is used.
[CollectionDefinition("UpdatePruneTests", DisableParallelization = true)]
public sealed class UpdatePruneTestsCollection;

[Collection("UpdatePruneTests")]
public sealed class UpdatePruneTests
{
    private readonly ITestOutputHelper output;
    public UpdatePruneTests(ITestOutputHelper output) => this.output = output;

    private const string ScratchRoot = @"D:\Workspace\_tmp\opencode\task034-r3-tests";
    private const string Tag = "v1.2.2";
    private const string SetupFile = "FlowDesk-1.2.2-win-x64-setup.exe";
    private const string SumsFile = "SHA256SUMS.txt";
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("prune test payload, never executed");

    // ---- Transport.PruneDownloads: owned layout ---------------------------------------

    [Fact]
    public void PopulatedVersionsAndLeavesAreRemovedWithTheRoot()
    {
        string temp = NewScratch("populated");
        try
        {
            string root = Root(temp);
            SeedLeaf(root, "1.2.0", Guid.NewGuid().ToString("N"), SetupFile);
            SeedLeaf(root, "1.2.0", Guid.NewGuid().ToString("N"), "extra.bin");
            SeedLeaf(root, "1.2.1", Guid.NewGuid().ToString("N"), SetupFile);
            SeedLeaf(root, "1.2.1", Guid.NewGuid().ToString("N"), "leftover.tmp");

            var logs = new List<Exception>();
            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.False(Directory.Exists(root));
            Assert.Empty(logs);
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public void EmptyGuidVersionAndRootAreRemovedNonrecursively()
    {
        string temp = NewScratch("empty-tree");
        try
        {
            string root = Root(temp);
            Directory.CreateDirectory(Path.Combine(root, "1.2.0"));
            Directory.CreateDirectory(Path.Combine(root, "1.2.1", Guid.NewGuid().ToString("N")));

            var logs = new List<Exception>();
            UpdateTransport.PruneDownloads(temp, logs.Add);
            Assert.False(Directory.Exists(root));
            Assert.Empty(logs);

            Directory.CreateDirectory(root);
            UpdateTransport.PruneDownloads(temp, logs.Add);
            Assert.False(Directory.Exists(root));
            Assert.Empty(logs);
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public void AbsentRootIsNotCreatedAndLogsNothing()
    {
        string temp = NewScratch("absent-root");
        try
        {
            string root = Root(temp);
            Assert.False(Directory.Exists(root));
            var logs = new List<Exception>();

            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.False(Directory.Exists(root));
            Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
            Assert.Empty(logs);
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public void LockedFilesAreRetainedWhileSafeSiblingsAreCleanedAndOnlyOneLineIsLogged()
    {
        string temp = NewScratch("locked");
        string lockedLeafA = Path.Combine(Root(temp), "1.2.0", Guid.NewGuid().ToString("N"));
        string lockedLeafB = Path.Combine(Root(temp), "1.2.0", Guid.NewGuid().ToString("N"));
        string unlockedLeaf = Path.Combine(Root(temp), "1.2.0", Guid.NewGuid().ToString("N"));
        string unlockedVersion = Path.Combine(Root(temp), "1.2.1");
        string unlockedVersionLeaf = Path.Combine(unlockedVersion, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lockedLeafA);
        Directory.CreateDirectory(lockedLeafB);
        Directory.CreateDirectory(unlockedLeaf);
        Directory.CreateDirectory(unlockedVersionLeaf);
        File.WriteAllText(Path.Combine(lockedLeafA, SetupFile), "locked a");
        File.WriteAllText(Path.Combine(lockedLeafB, SetupFile), "locked b");
        File.WriteAllText(Path.Combine(unlockedLeaf, SetupFile), "free");
        File.WriteAllText(Path.Combine(unlockedVersionLeaf, SetupFile), "free");

        using var lockA = new FileStream(Path.Combine(lockedLeafA, SetupFile), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var lockB = new FileStream(Path.Combine(lockedLeafB, SetupFile), FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var logs = new List<Exception>();
            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.Single(logs);
            Assert.True(File.Exists(Path.Combine(lockedLeafA, SetupFile)));
            Assert.True(File.Exists(Path.Combine(lockedLeafB, SetupFile)));
            Assert.True(Directory.Exists(lockedLeafA));
            Assert.True(Directory.Exists(lockedLeafB));
            Assert.Equal(2, Directory.EnumerateDirectories(Path.Combine(Root(temp), "1.2.0")).Count());
            Assert.False(Directory.Exists(unlockedLeaf));
            Assert.False(Directory.Exists(unlockedVersion));
            Assert.True(Directory.Exists(Root(temp)));

            lockA.Dispose();
            lockB.Dispose();
            logs.Clear();
            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.Empty(logs);
            Assert.False(Directory.Exists(Root(temp)));
        }
        finally
        {
            lockA.Dispose();
            lockB.Dispose();
            TryDeleteTree(temp);
        }
    }

    [Fact]
    public void NonMatchingEntriesAndNestedDirectoriesAreLeftAlone()
    {
        string temp = NewScratch("nonmatching");
        try
        {
            string root = Root(temp);
            string unknownRootDir = Path.Combine(root, "not-a-version");
            string rootFile = Path.Combine(root, "readme.txt");
            string version = Path.Combine(root, "1.2.1");
            string notes = Path.Combine(version, "notes.txt");
            string extras = Path.Combine(version, "extras");
            string junk = Path.Combine(version, "junk");
            string leaf = Path.Combine(version, Guid.NewGuid().ToString("N"));
            string nested = Path.Combine(leaf, "nested");
            string emptyLeaf = Path.Combine(version, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unknownRootDir);
            Directory.CreateDirectory(extras);
            Directory.CreateDirectory(junk);
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(emptyLeaf);
            File.WriteAllText(Path.Combine(unknownRootDir, "marker.txt"), "keep root dir");
            File.WriteAllText(rootFile, "keep root file");
            File.WriteAllText(notes, "keep version file");
            File.WriteAllText(Path.Combine(extras, "marker.txt"), "keep version dir");
            File.WriteAllText(Path.Combine(junk, "marker.txt"), "keep unknown version dir");
            File.WriteAllText(Path.Combine(leaf, SetupFile), "delete me");
            File.WriteAllText(Path.Combine(leaf, "payload.bin"), "delete me too");
            File.WriteAllText(Path.Combine(nested, "marker.txt"), "keep nested");

            var logs = new List<Exception>();
            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.Empty(logs);
            Assert.True(Directory.Exists(root));
            Assert.Equal("keep root dir", File.ReadAllText(Path.Combine(unknownRootDir, "marker.txt")));
            Assert.Equal("keep root file", File.ReadAllText(rootFile));
            Assert.Equal("keep version file", File.ReadAllText(notes));
            Assert.Equal("keep version dir", File.ReadAllText(Path.Combine(extras, "marker.txt")));
            Assert.Equal("keep unknown version dir", File.ReadAllText(Path.Combine(junk, "marker.txt")));
            Assert.False(File.Exists(Path.Combine(leaf, SetupFile)));
            Assert.False(File.Exists(Path.Combine(leaf, "payload.bin")));
            Assert.True(Directory.Exists(nested));
            Assert.Equal("keep nested", File.ReadAllText(Path.Combine(nested, "marker.txt")));
            Assert.False(Directory.Exists(emptyLeaf));
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public void ParsedVersionFolderNamesAreRecognizedInCanonicalForm()
    {
        string temp = NewScratch("canonical-versions");
        try
        {
            string root = Root(temp);
            string[] owned = ["1.2.0", "2.5.9", "3.0.0-rc.1"];
            string[] ignored = ["01.2.3", "1.2", "release", "1.2.3.4"];
            foreach (string version in owned.Concat(ignored))
            {
                string leaf = Path.Combine(root, version, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(leaf);
                File.WriteAllText(Path.Combine(leaf, SetupFile), version);
            }

            var logs = new List<Exception>();
            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.Empty(logs);
            Assert.True(Directory.Exists(root));
            foreach (string version in owned) Assert.False(Directory.Exists(Path.Combine(root, version)));
            foreach (string version in ignored) Assert.True(Directory.Exists(Path.Combine(root, version)));
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public void ReparseRootIsRejectedWithoutTouchingItsTarget()
    {
        string temp = NewScratch("reparse-root");
        string? link = null;
        try
        {
            string target = Path.Combine(temp, "external-root");
            Directory.CreateDirectory(Path.Combine(target, "1.2.0", Guid.NewGuid().ToString("N")));
            string marker = Path.Combine(target, "marker.txt");
            File.WriteAllText(marker, "outside");
            link = Root(temp);
            if (!TryCreateJunction(link, target))
            {
                NoteJunction("root", false, link);
                output.WriteLine("NOT RUN: this environment cannot create a junction or directory symlink.");
                return;
            }
            NoteJunction("root", true, link);
            output.WriteLine("RUN: reparse root created at " + link);

            var logs = new List<Exception>();
            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.True(logs.Count <= 1);
            Assert.Equal("outside", File.ReadAllText(marker));
            Assert.True(Directory.Exists(Path.Combine(target, "1.2.0")));
            Assert.Single(Directory.EnumerateFiles(target, "*.txt", SearchOption.AllDirectories));
        }
        finally
        {
            DeleteLink(link);
            TryDeleteTree(temp);
        }
    }

    [Fact]
    public void ReparseVersionIsRejectedWhileSafeVersionsAreCleaned()
    {
        string temp = NewScratch("reparse-version");
        string? link = null;
        try
        {
            string root = Root(temp);
            Directory.CreateDirectory(root);
            string target = Path.Combine(temp, "external-version");
            string targetLeaf = Path.Combine(target, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(targetLeaf);
            string marker = Path.Combine(target, "marker.txt");
            File.WriteAllText(marker, "outside version");
            link = Path.Combine(root, "1.2.1");
            if (!TryCreateJunction(link, target))
            {
                NoteJunction("version", false, link);
                output.WriteLine("NOT RUN: this environment cannot create a junction or directory symlink.");
                return;
            }
            NoteJunction("version", true, link);
            output.WriteLine("RUN: reparse version created at " + link);
            SeedLeaf(root, "1.2.0", Guid.NewGuid().ToString("N"), SetupFile);

            var logs = new List<Exception>();
            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.True(logs.Count <= 1);
            Assert.Equal("outside version", File.ReadAllText(marker));
            Assert.True(Directory.Exists(targetLeaf));
            Assert.False(Directory.Exists(Path.Combine(root, "1.2.0")));
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            DeleteLink(link);
            TryDeleteTree(temp);
        }
    }

    [Fact]
    public void ReparseLeafIsRejectedWhileSafeLeavesAreCleaned()
    {
        string temp = NewScratch("reparse-leaf");
        string? link = null;
        try
        {
            string root = Root(temp);
            string version = Path.Combine(root, "1.2.1");
            Directory.CreateDirectory(version);
            string target = Path.Combine(temp, "external-leaf");
            Directory.CreateDirectory(target);
            string marker = Path.Combine(target, "marker.txt");
            File.WriteAllText(marker, "outside leaf");
            link = Path.Combine(version, Guid.NewGuid().ToString("N"));
            if (!TryCreateJunction(link, target))
            {
                NoteJunction("leaf", false, link);
                output.WriteLine("NOT RUN: this environment cannot create a junction or directory symlink.");
                return;
            }
            NoteJunction("leaf", true, link);
            output.WriteLine("RUN: reparse leaf created at " + link);
            string safe = Path.Combine(version, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(safe);
            File.WriteAllText(Path.Combine(safe, SetupFile), "delete");

            var logs = new List<Exception>();
            UpdateTransport.PruneDownloads(temp, logs.Add);

            Assert.True(logs.Count <= 1);
            Assert.Equal("outside leaf", File.ReadAllText(marker));
            Assert.True(Directory.Exists(link));
            Assert.False(Directory.Exists(safe));
            Assert.True(Directory.Exists(version));
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            DeleteLink(link);
            TryDeleteTree(temp);
        }
    }

    [Fact]
    public void PreCancelledTokenThrowsWithoutDeletingAnything()
    {
        string temp = NewScratch("cancelled");
        try
        {
            string leaf = SeedLeaf(Root(temp), "1.2.0", Guid.NewGuid().ToString("N"), SetupFile);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var logs = new List<Exception>();

            Assert.ThrowsAny<OperationCanceledException>(
                () => UpdateTransport.PruneDownloads(temp, logs.Add, cts.Token));

            Assert.Empty(logs);
            Assert.True(File.Exists(Path.Combine(leaf, SetupFile)));
        }
        finally { TryDeleteTree(temp); }
    }

    // ---- Coordinator startup prune -----------------------------------------------------

    [Fact]
    public async Task UpdatesOffStillRunsTheInjectedPruneOnceAndMakesNoHttp()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Updates are off"));
        using var harness = new PruneHarness(handler, new AppSettings { CheckForUpdates = false },
            prune: _ => Task.CompletedTask);

        await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, harness.PruneCount);
        Assert.Equal(0, handler.Count);
        Assert.Equal(0, harness.PersistCount);
        Assert.Null(harness.Coordinator.State.Pending);
        Assert.Empty(harness.Logs);
    }

    [Fact]
    public async Task UpdatesOffRealTempPruneRemovesTheOwnedTree()
    {
        string temp = NewScratch("off-real-prune");
        using var scope = new TempScope(temp);
        try
        {
            string leaf = SeedLeaf(Root(temp), "1.2.0", Guid.NewGuid().ToString("N"), SetupFile);
            Assert.True(File.Exists(Path.Combine(leaf, SetupFile)));
            var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Updates are off"));
            using var harness = new PruneHarness(handler, new AppSettings { CheckForUpdates = false });

            await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(Directory.Exists(Root(temp)));
            Assert.Equal(0, handler.Count);
            Assert.Empty(harness.Logs);
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public async Task StartupPruneIsAttemptedOnlyOncePerProcess()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP for cleanup"));
        using var harness = new PruneHarness(handler, prune: _ => Task.CompletedTask);

        await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, harness.PruneCount);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task InstallIsBlockedWhilePruneRunsThenWorksAfterwards()
    {
        string temp = NewScratch("install-blocked-prune");
        using var scope = new TempScope(temp);
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = FeedAndDownloadHandler();
            using var harness = new PruneHarness(handler, prune: async token =>
            {
                entered.TrySetResult();
                await gate.Task.WaitAsync(token);
            });
            await harness.Coordinator.CheckIfDueAsync(FixedNow).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(harness.Coordinator.State.Pending);
            Assert.True(harness.Coordinator.CanInstall);

            Task prune = harness.Coordinator.PruneAtStartupAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(harness.Coordinator.CanInstall);
            int before = handler.Count;
            await harness.Coordinator.InstallAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, handler.Count);
            Assert.Equal(0, harness.HandoffCount);

            gate.SetResult();
            await prune.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(harness.Coordinator.CanInstall);

            await harness.Coordinator.InstallAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, harness.HandoffCount);
            Assert.True(harness.Coordinator.State.Pending is not null);
            Assert.False(harness.Coordinator.State.Busy);
            Assert.Empty(Directory.EnumerateFiles(temp, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public async Task BusyInstallSkipsTheStartupPruneEntirely()
    {
        string temp = NewScratch("busy-handoff-prune");
        using var scope = new TempScope(temp);
        try
        {
            var handoffEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handoffGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = FeedAndDownloadHandler();
            using var harness = new PruneHarness(handler, prune: _ => Task.CompletedTask);
            harness.Handoff = async _ =>
            {
                handoffEntered.TrySetResult();
                await handoffGate.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return true;
            };
            await harness.Coordinator.CheckIfDueAsync(FixedNow).WaitAsync(TimeSpan.FromSeconds(5));
            Task install = harness.Coordinator.InstallAsync();
            await handoffEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(harness.Coordinator.State.Busy);

            await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, harness.PruneCount);

            handoffGate.SetResult();
            await install.WaitAsync(TimeSpan.FromSeconds(5));
            await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, harness.PruneCount); // skipped this run; retries at the next app start
            Assert.Equal(1, harness.HandoffCount);
            Assert.Empty(harness.Logs);
            Assert.Empty(Directory.EnumerateFiles(temp, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public async Task BusyDownloadSkipsTheStartupPruneEntirely()
    {
        string temp = NewScratch("busy-download-prune");
        using var scope = new TempScope(temp);
        try
        {
            var downloadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var downloadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new FakeHandler(async (request, token) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Ok(Manifest());
                if (path.EndsWith(SetupFile, StringComparison.Ordinal))
                {
                    downloadEntered.TrySetResult();
                    await downloadGate.Task.WaitAsync(token);
                    return Raw(InstallerBytes);
                }
                return Ok(ReleaseJson(Tag));
            });
            using var harness = new PruneHarness(handler, prune: _ => Task.CompletedTask);
            await harness.Coordinator.CheckIfDueAsync(FixedNow).WaitAsync(TimeSpan.FromSeconds(5));
            Task install = harness.Coordinator.InstallAsync();
            await downloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(harness.Coordinator.State.Busy);

            await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, harness.PruneCount);

            downloadGate.SetResult();
            await install.WaitAsync(TimeSpan.FromSeconds(5));
            await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, harness.PruneCount); // skipped this run; retries at the next app start
            Assert.Equal(1, harness.HandoffCount);
            Assert.Empty(harness.Logs);
            Assert.Empty(Directory.EnumerateFiles(temp, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(temp); }
    }

    [Fact]
    public async Task CleanupFailureLogsOnceAndNeverSetsUiError()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP for cleanup"));
        using var harness = new PruneHarness(handler,
            prune: _ => throw new IOException("cleanup failed"));

        await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(harness.Logs);
        Assert.IsType<IOException>(harness.Logs[0]);
        Assert.Null(harness.Coordinator.State.Error);
        Assert.Null(harness.Coordinator.State.Pending);
        Assert.False(harness.Coordinator.State.Busy);
        Assert.Equal(0, handler.Count);
        Assert.Empty(harness.States);
    }

    [Fact]
    public async Task StopCancelsCleanupWithoutUiOrLog()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP for cleanup"));
        using var harness = new PruneHarness(handler, prune: async token =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
        });

        Task prune = harness.Coordinator.PruneAtStartupAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Coordinator.Stop();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await prune.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(harness.Logs);
        Assert.Null(harness.Coordinator.State.Error);
        Assert.False(harness.Coordinator.CanInstall);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task PruneDoesNotPersistSettingsOrTouchTimestamps()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP for cleanup"));
        var settings = new AppSettings
        {
            LastUpdateCheck = FixedNow.AddHours(-3),
            SkippedUpdateVersion = "9.9.9",
        };
        using var harness = new PruneHarness(handler, settings, prune: _ => Task.CompletedTask);

        await harness.Coordinator.PruneAtStartupAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FixedNow.AddHours(-3), settings.LastUpdateCheck);
        Assert.Equal("9.9.9", settings.SkippedUpdateVersion);
        Assert.Equal(0, harness.PersistCount);
        Assert.Equal(0, handler.Count);
        Assert.Equal(1, harness.PruneCount);
    }

    [Fact]
    public async Task StartingThenStoppingDuringTheStartDelayIsSilent()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP before the delay"));
        using var harness = new PruneHarness(handler, prune: _ => Task.CompletedTask);

        harness.Coordinator.Start();
        await Task.Delay(100);
        harness.Coordinator.Stop();
        await Task.Delay(200);

        Assert.Equal(0, harness.PruneCount);
        Assert.Equal(0, handler.Count);
        Assert.Empty(harness.Logs);
    }

    [Fact]
    public void RunAsyncDelaysThirtySecondsThenPrunesBeforeTheFirstHttpCheck()
    {
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "AppSources", "Services", "UpdateCoordinator.cs"));

        int run = source.IndexOf("private async Task RunAsync()", StringComparison.Ordinal);
        Assert.True(run >= 0, "RunAsync must exist");
        int delay = source.IndexOf("Task.Delay(TimeSpan.FromSeconds(30)", run, StringComparison.Ordinal);
        Assert.True(delay > run, "RunAsync must wait 30 s before its first maintenance step");
        int prune = source.IndexOf("await PruneAtStartupAsync();", delay, StringComparison.Ordinal);
        Assert.True(prune > delay, "startup prune must follow the 30 s start delay");
        int check = source.IndexOf("await CheckIfDueAsync(DateTimeOffset.UtcNow);", prune, StringComparison.Ordinal);
        Assert.True(check > prune, "the first HTTP check must come after the startup prune");
    }

    // ---- Helpers ----------------------------------------------------------------------

    private static string Root(string temp) => Path.Combine(temp, "FlowDesk-Update");

    private static string SeedLeaf(string root, string version, string leafName, string fileName)
    {
        string leaf = Path.Combine(root, version, leafName);
        Directory.CreateDirectory(leaf);
        File.WriteAllText(Path.Combine(leaf, fileName), "seed");
        return leaf;
    }

    private static FakeHandler FeedAndDownloadHandler()
    {
        return new FakeHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
            if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
            return Task.FromResult(Ok(ReleaseJson(Tag)));
        });
    }

    private static string Manifest() => Convert.ToHexString(SHA256.HashData(InstallerBytes)) + "  " + SetupFile + "\n";

    private static void NoteJunction(string level, bool created, string path)
    {
        Directory.CreateDirectory(ScratchRoot);
        File.AppendAllText(Path.Combine(ScratchRoot, "junction-evidence.txt"),
            level + ": " + (created ? "RUN (link created)" : "NOT RUN (no junction/symlink privilege)") +
            " at " + path + "\n");
    }

    private static string AssetUrl(string tag, string file) =>
        "https://github.com/kiraa000/FlowDesk/releases/download/" + tag + "/" + file;

    private static string ReleaseJson(string tag)
    {
        string version = tag.TrimStart('v');
        string setup = "FlowDesk-" + version + "-win-x64-setup.exe";
        return "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"html_url\":\""
            + "https://github.com/kiraa000/FlowDesk/releases/tag/" + tag + "\",\"assets\":["
            + "{\"name\":\"" + setup + "\",\"browser_download_url\":\"" + AssetUrl(tag, setup) + "\"},"
            + "{\"name\":\"" + SumsFile + "\",\"browser_download_url\":\"" + AssetUrl(tag, SumsFile) + "\"}]}";
    }

    private static UpdateTransport NewTransport(string version, HttpMessageHandler handler)
    {
        string? old = Environment.GetEnvironmentVariable("FLOWDESK_UPDATE_FEED");
        try
        {
            Environment.SetEnvironmentVariable("FLOWDESK_UPDATE_FEED", null);
            return new UpdateTransport(version, handler);
        }
        finally { Environment.SetEnvironmentVariable("FLOWDESK_UPDATE_FEED", old); }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Raw(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static string NewScratch(string name)
    {
        string dir = Path.Combine(ScratchRoot, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteLink(string? link)
    {
        if (link is null || !Directory.Exists(link)) return;
        try { Directory.Delete(link); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteTree(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }

        try
        {
            var info = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process? process = Process.Start(info);
            if (process is null) return false;
            process.WaitForExit(5000);
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private int count;
        public int Count => Volatile.Read(ref count);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            return await responder(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class PruneHarness : IDisposable
    {
        public AppSettings Settings { get; }
        public FakeHandler Handler { get; }
        public List<Exception> Logs { get; } = [];
        public List<UpdateState> States { get; } = [];
        public int PersistCount { get; private set; }
        public int HandoffCount { get; private set; }
        public int PruneCount { get; private set; }
        public Func<VerifiedInstaller, Task<bool>> Handoff { get; set; } = _ => Task.FromResult(true);
        public UpdateTransport Transport { get; }
        public UpdateCoordinator Coordinator { get; }

        public PruneHarness(FakeHandler handler, AppSettings? settings = null,
            Func<CancellationToken, Task>? prune = null)
        {
            Handler = handler;
            Settings = settings ?? new AppSettings();
            Transport = NewTransport("1.2.0", handler);
            Func<CancellationToken, Task>? injected = prune is null
                ? null
                : async token =>
                {
                    PruneCount++;
                    await prune(token);
                };
            Coordinator = new UpdateCoordinator(Transport, "1.2.0", () => Settings,
                () => PersistCount++, exception => Logs.Add(exception),
                installer => { HandoffCount++; return Handoff(installer); }, injected);
            Coordinator.StateChanged += () => States.Add(Coordinator.State);
        }

        public void Dispose() => Coordinator.Dispose();
    }

    private sealed class TempScope : IDisposable
    {
        private readonly string? tmp, temp;
        public TempScope(string path)
        {
            Directory.CreateDirectory(path);
            tmp = Environment.GetEnvironmentVariable("TMP");
            temp = Environment.GetEnvironmentVariable("TEMP");
            Environment.SetEnvironmentVariable("TMP", path);
            Environment.SetEnvironmentVariable("TEMP", path);
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("TMP", tmp);
            Environment.SetEnvironmentVariable("TEMP", temp);
        }
    }
}
