using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlowDesk.App.Services;
using FlowDesk.Core.Updates;
using Xunit.Abstractions;

namespace FlowDesk.App.Tests;

// The update transport and coordinator are UI-thread-free, so they are tested headless with a fake
// HttpMessageHandler. Nothing here touches GitHub, a real installer or a real FlowDesk process.
[CollectionDefinition("UpdateTests", DisableParallelization = true)]
public sealed class UpdateTestsCollection;

[Collection("UpdateTests")]
public sealed class UpdateTests
{
    private readonly ITestOutputHelper output;
    public UpdateTests(ITestOutputHelper output) => this.output = output;

    private const string Tag = "v1.2.1";
    private const string SetupFile = "FlowDesk-1.2.1-win-x64-setup.exe";
    private const string SumsFile = "SHA256SUMS.txt";
    private const string ScratchRoot = @"D:\Workspace\_tmp\opencode\task034-tests";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("fake installer payload, never executed");

    // ---- Asset and redirect URL policy -------------------------------------------------

    [Theory]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/FlowDesk-1.2.1-win-x64-setup.exe", true)]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt", true)]
    [InlineData("http://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt", false)]
    [InlineData("https://evil.example/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt", false)]
    [InlineData("https://github.com.evil.example/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt", false)]
    [InlineData("https://github.com/other/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt", false)]
    [InlineData("https://github.com/kiraa000/flowdesk/releases/download/v1.2.1/SHA256SUMS.txt", false)]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/download/v1.2.0/SHA256SUMS.txt", false)]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/tag/v1.2.1", false)]
    [InlineData("https://user:pass@github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt", false)]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt#fragment", false)]
    [InlineData("https://github.com:8443/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt", false)]
    public void AssetUrlMustBeExactHttpsRepoAndTag(string url, bool allowed)
    {
        using var transport = NewTransport("1.2.0", new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP in a policy test")));
        Assert.Equal(allowed, transport.IsAssetAllowed(new Uri(url), Tag));
    }

    [Fact]
    public void AssetUrlRejectsMissingAndRelativeValues()
    {
        using var transport = NewTransport("1.2.0", new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP in a policy test")));
        Assert.False(transport.IsAssetAllowed(null, Tag));
        Assert.False(transport.IsAssetAllowed(new Uri("/kiraa000/FlowDesk/releases/download/v1.2.1/x", UriKind.Relative), Tag));
    }

    [Theory]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/x", true)]
    [InlineData("https://objects.githubusercontent.com/a/b", true)]
    [InlineData("https://release-assets.githubusercontent.com/a/b", true)]
    [InlineData("http://objects.githubusercontent.com/a/b", false)]
    [InlineData("https://evil.example/a/b", false)]
    [InlineData("https://objects.githubusercontent.com.evil.example/a/b", false)]
    [InlineData("https://user:pass@objects.githubusercontent.com/a/b", false)]
    [InlineData("https://objects.githubusercontent.com/a/b#fragment", false)]
    [InlineData("https://objects.githubusercontent.com:8443/a/b", false)]
    public void RedirectTargetsAllowOnlyApprovedHttpsHosts(string url, bool allowed)
    {
        using var transport = NewTransport("1.2.0", new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP in a policy test")));
        Assert.Equal(allowed, transport.IsRedirectAllowed(new Uri(url)));
    }

    [Theory]
    [InlineData("http://objects.githubusercontent.com/redirect")]
    [InlineData("https://evil.example/redirect")]
    public async Task BadRedirectOriginIsRefusedBeforeTheNextRequest(string location)
    {
        string dir = NewScratch("bad-redirect");
        try
        {
            var handler = new FakeHandler((_, _) => Task.FromResult(Redirect(location)));
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None));
            Assert.Equal("Update redirect origin", ex.Message);
            Assert.Equal(1, handler.Count);
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task FiveRedirectsAreFollowedAndTheInstallerStillVerifies()
    {
        string dir = NewScratch("five-redirects");
        try
        {
            int sumsRedirects = 0;
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal) && sumsRedirects < 5)
                {
                    sumsRedirects++;
                    return Task.FromResult(Redirect($"https://objects.githubusercontent.com/blob/{sumsRedirects}/{SumsFile}"));
                }
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var transport = NewTransport("1.2.0", handler);
            using var verified = await transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None);
            Assert.True(File.Exists(verified.Path));
            Assert.Equal(5, sumsRedirects);
            Assert.Equal(7, handler.Count); // six checksum requests (five redirects) plus one installer
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task SixthRedirectIsRejected()
    {
        string dir = NewScratch("six-redirects");
        try
        {
            int redirects = 0;
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal))
                {
                    redirects++;
                    return Task.FromResult(Redirect($"https://objects.githubusercontent.com/blob/{redirects}/{SumsFile}"));
                }
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None));
            Assert.Equal("Update redirect limit", ex.Message);
            Assert.Equal(6, redirects); // five were followed; the sixth response was refused, not followed
            Assert.Equal(6, handler.Count);
        }
        finally { TryDeleteTree(dir); }
    }

    // ---- Feed -------------------------------------------------------------------------

    [Fact]
    public async Task FeedRequestSendsOnlyTheGitHubHeaders()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Ok(FeedJson("v1.2.0"))));
        using var transport = NewTransport("1.2.0", handler);
        UpdateRelease? release = await transport.CheckAsync(CancellationToken.None);
        Assert.NotNull(release);
        SeenRequest request = Assert.Single(handler.Seen);
        Assert.Equal(HttpMethod.Get.Method, request.Method);
        Assert.Equal(UpdateTransport.FeedUrl, request.Uri.AbsoluteUri);
        Assert.Equal("application/vnd.github+json", request.Accept);
        Assert.Equal("FlowDesk-Updater/1.2.0", request.UserAgent);
    }

    [Fact]
    public async Task FeedFailureStatusThrowsForTheCoordinatorToLog()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Status(HttpStatusCode.InternalServerError)));
        using var transport = NewTransport("1.2.0", handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.CheckAsync(CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"tag_name\":\"v1.2.2\",\"draft\":true}")]
    [InlineData("{\"tag_name\":\"v1.2.2\",\"prerelease\":true}")]
    [InlineData("{\"tag_name\":\"not-a-version\"}")]
    public async Task MalformedDraftPrereleaseAndBadTagsProduceNoRelease(string body)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Ok(body)));
        using var transport = NewTransport("1.2.0", handler);
        Assert.Null(await transport.CheckAsync(CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task FeedContentLengthCapRejectsBeforeReadingTheBody()
    {
        long cap = UpdateTransport.FeedLimit + 1;
        var handler = new FakeHandler((_, _) => Task.FromResult(DeclaredLength(cap)));
        using var transport = NewTransport("1.2.0", handler);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => transport.CheckAsync(CancellationToken.None));
        Assert.Equal("Update content length", ex.Message);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task FeedStreamingCapRejectsUnknownLengthBody()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Unknown(new byte[UpdateTransport.FeedLimit + 1]) }));
        using var transport = NewTransport("1.2.0", handler);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => transport.CheckAsync(CancellationToken.None));
        Assert.Equal("Update size limit", ex.Message);
        Assert.Equal(1, handler.Count);
    }

    // ---- PrepareAsync: caps, verification and the locked installer ---------------------

    [Fact]
    public async Task ChecksumContentLengthCapIsRejectedBeforeAnyRead()
    {
        string dir = NewScratch("checksum-length");
        try
        {
            var handler = new FakeHandler((request, _) =>
                request.RequestUri!.AbsolutePath.EndsWith(SumsFile, StringComparison.Ordinal)
                    ? Task.FromResult(DeclaredLength(UpdateTransport.ChecksumLimit + 1))
                    : Task.FromResult(Status(HttpStatusCode.NotFound)));
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None));
            Assert.Equal("Update content length", ex.Message);
            Assert.Equal(1, handler.Count);
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task InstallerContentLengthCapIsRejectedWithoutAllocatingTheBody()
    {
        string dir = NewScratch("installer-length");
        try
        {
            var handler = new FakeHandler((request, _) =>
                request.RequestUri!.AbsolutePath.EndsWith(SumsFile, StringComparison.Ordinal)
                    ? Task.FromResult(Ok(Manifest()))
                    : Task.FromResult(DeclaredLength(UpdateTransport.InstallerLimit + 1)));
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None));
            Assert.Equal("Update content length", ex.Message);
            Assert.Equal(2, handler.Count);
            Assert.Empty(Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task ChecksumStreamingCapRejectsUnknownLengthManifest()
    {
        string dir = NewScratch("checksum-stream");
        try
        {
            var handler = new FakeHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = Unknown(new byte[UpdateTransport.ChecksumLimit + 1]) }));
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None));
            Assert.Equal("Update size limit", ex.Message);
            Assert.Equal(1, handler.Count);
            Assert.Empty(Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task PrepareVerifiesTheBytesAndPinsTheFileUntilLaunch()
    {
        string dir = NewScratch("prepare-verified");
        try
        {
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var transport = NewTransport("1.2.0", handler);
            var installer = await transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None);
            string path = installer.Path;
            try
            {
                Assert.True(File.Exists(path));
                Assert.Equal(InstallerBytes, await File.ReadAllBytesAsync(path));

                var writeAttempt = Record.Exception(() =>
                {
                    using var _ = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                });
                Assert.NotNull(writeAttempt);
                Assert.True(writeAttempt is IOException or UnauthorizedAccessException, writeAttempt!.GetType().Name);

                var deleteAttempt = Record.Exception(() => File.Delete(path));
                Assert.NotNull(deleteAttempt);
                Assert.True(deleteAttempt is IOException or UnauthorizedAccessException, deleteAttempt!.GetType().Name);

                ProcessStartInfo? captured = null;
                Assert.True(installer.Launch(info => { captured = info; return true; }));
                Assert.NotNull(captured);
                Assert.False(captured!.UseShellExecute);
                Assert.Equal(path, captured.FileName);
                Assert.Equal(Path.GetDirectoryName(path), captured.WorkingDirectory);
                string[] args = captured.ArgumentList.ToArray();
                foreach (string required in new[]
                         { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/RELAUNCH", "/FLOWDESKPID=" + Environment.ProcessId })
                    Assert.Contains(required, args);
            }
            finally { installer.Dispose(); }

            // A successful hand-off must not delete the file the installer is about to read.
            Assert.True(File.Exists(path));
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task HashMismatchUsesTheExactMessageAndLeavesNoExecutable()
    {
        string dir = NewScratch("hash-mismatch");
        try
        {
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal))
                    return Task.FromResult(Ok(new string('0', 64) + "  " + SetupFile + "\n"));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<UpdateVerificationException>(
                () => transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None));
            Assert.Equal("The download did not match its checksum and was deleted.", ex.Message);
            Assert.Empty(Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task CancelDuringInstallerReadDeletesThePartialFileAndNeverLaunches()
    {
        string dir = NewScratch("cancel-read");
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new GatedStream([1, 2, 3], started, release)),
                    });
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var transport = NewTransport("1.2.0", handler);
            using var cts = new CancellationTokenSource();
            Task<VerifiedInstaller> prepare = transport.PrepareAsync(MakeRelease(), dir, null, cts.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepare);
            Assert.Empty(Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories));
            Assert.False(release.Task.IsCompleted);
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task LaunchFailureReturnsFalseAndDisposeDeletesTheDownload()
    {
        string dir = NewScratch("launch-fail");
        try
        {
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var transport = NewTransport("1.2.0", handler);
            var installer = await transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None);
            string path = installer.Path;
            Assert.False(installer.Launch(_ => false));
            installer.Dispose();
            Assert.False(File.Exists(path));
            Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        }
        finally { TryDeleteTree(dir); }
    }

    [Theory]
    [InlineData("missing-installer")]
    [InlineData("missing-checksums")]
    [InlineData("bad-prefix")]
    public async Task MissingOrWrongPrefixAssetsNeverTouchTheNetwork(string kind)
    {
        string dir = NewScratch("missing-asset");
        try
        {
            UpdateRelease release = MakeRelease();
            release = kind switch
            {
                "missing-installer" => release with { Installer = null },
                "missing-checksums" => release with { Checksums = null },
                _ => release with { Installer = new Uri(AssetUrl("v1.2.0", SetupFile)) },
            };
            var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Assets must be rejected before HTTP"));
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<UpdateVerificationException>(
                () => transport.PrepareAsync(release, dir, null, CancellationToken.None));
            Assert.Equal("This update cannot be checked. Download it from the releases page.", ex.Message);
            Assert.Equal(0, handler.Count);
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task DuplicateChecksumEntryPreventsTheInstallerDownload()
    {
        string dir = NewScratch("duplicate-sum");
        try
        {
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal))
                    return Task.FromResult(Ok(Manifest() + Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<UpdateVerificationException>(
                () => transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None));
            Assert.Equal("This update cannot be checked. Download it from the releases page.", ex.Message);
            Assert.Equal(1, handler.Count);
            Assert.EndsWith(SumsFile, handler.Seen[0].Uri.AbsolutePath);
        }
        finally { TryDeleteTree(dir); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingReparseRootOrVersionFolderIsRejected(bool atRoot)
    {
        string dir = NewScratch(atRoot ? "reparse-root" : "reparse-version");
        string? link = null;
        try
        {
            string target = Path.Combine(dir, "junction-target");
            Directory.CreateDirectory(target);
            string root = Path.Combine(dir, "FlowDesk-Update");
            if (atRoot) link = root;
            else { Directory.CreateDirectory(root); link = Path.Combine(root, "1.2.1"); }
            if (!TryCreateJunction(link, target))
            {
                output.WriteLine("NOT RUN: this environment cannot create a junction or directory symlink.");
                File.AppendAllText(Path.Combine(ScratchRoot, "reparse-check.txt"),
                    (atRoot ? "root" : "version") + ": NOT RUN (no junction/symlink privilege)\n");
                return;
            }
            output.WriteLine("RUN: reparse link created at " + link);
            File.AppendAllText(Path.Combine(ScratchRoot, "reparse-check.txt"),
                (atRoot ? "root" : "version") + ": RUN (link " + link + ")\n");

            int installerRequests = 0;
            var handler = new FakeHandler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith(SetupFile, StringComparison.Ordinal))
                {
                    installerRequests++;
                    return Task.FromResult(Raw(InstallerBytes));
                }
                return Task.FromResult(Ok(Manifest()));
            });
            using var transport = NewTransport("1.2.0", handler);
            var ex = await Assert.ThrowsAsync<IOException>(
                () => transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None));
            Assert.Equal("Unsafe update directory", ex.Message);
            Assert.Equal(0, installerRequests);
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            if (link is not null && Directory.Exists(link))
            {
                try { Directory.Delete(link); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            TryDeleteTree(dir);
        }
    }

    // ---- Coordinator lifecycle ---------------------------------------------------------

    [Fact]
    public async Task SettingsOffMakesZeroHttpAndInstallCalls()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP when updates are off"));
        using var harness = new CoordinatorHarness(handler, settings: new AppSettings { CheckForUpdates = false });
        await harness.Coordinator.CheckIfDueAsync(Now);
        await harness.Coordinator.InstallAsync();
        Assert.Equal(0, handler.Count);
        Assert.Equal(0, harness.HandoffCount);
        Assert.Equal(0, harness.PersistCount);
        Assert.Null(harness.Coordinator.State.Pending);
    }

    [Fact]
    public async Task CheckIfDueRunsImmediatelyForTestsWithoutTheStartDelay()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Ok(FeedJson("v1.2.2"))));
        using var harness = new CoordinatorHarness(handler);
        await harness.Coordinator.CheckIfDueAsync(Now).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.Count);
        Assert.NotNull(harness.Coordinator.State.Pending);
    }

    [Fact]
    public async Task RecentCheckSkipsNetworkPersistAndState()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP within 24 h"));
        using var harness = new CoordinatorHarness(handler, settings: new AppSettings { LastUpdateCheck = Now.AddHours(-1) });
        await harness.Coordinator.CheckIfDueAsync(Now);
        Assert.Equal(0, handler.Count);
        Assert.Equal(0, harness.PersistCount);
        Assert.Empty(harness.States);
        Assert.Null(harness.Coordinator.State.Pending);
    }

    [Theory]
    [InlineData("v1.2.2", true)]
    [InlineData("v1.2.1", false)]
    [InlineData("v1.2.0", false)]
    public async Task DueCheckPersistsTheTimeAndOffersOnlyStrictlyNewer(string tag, bool offered)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Ok(FeedJson(tag))));
        using var harness = new CoordinatorHarness(handler, runningVersion: "1.2.1");
        harness.Settings.LastUpdateCheck = Now.AddHours(-25);
        await harness.Coordinator.CheckIfDueAsync(Now);
        Assert.Equal(1, handler.Count);
        Assert.Equal(1, harness.PersistCount);
        Assert.Equal(Now, harness.Settings.LastUpdateCheck);
        Assert.Equal(offered, harness.Coordinator.State.Pending is not null);
        Assert.Null(harness.Coordinator.State.Error);
    }

    [Fact]
    public async Task SkippedVersionIsNotOfferedAgain()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Ok(FeedJson("v1.2.2"))));
        using var harness = new CoordinatorHarness(handler);
        harness.Settings.SkippedUpdateVersion = "1.2.2";
        await harness.Coordinator.CheckIfDueAsync(Now);
        Assert.Equal(1, handler.Count);
        Assert.Equal(1, harness.PersistCount);
        Assert.Null(harness.Coordinator.State.Pending);
        Assert.Empty(harness.States);
    }

    [Fact]
    public async Task FailedCheckLogsOnceAdvancesTheTimeAndShowsNoUiError()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Status(HttpStatusCode.Forbidden)));
        using var harness = new CoordinatorHarness(handler);
        harness.Settings.LastUpdateCheck = Now.AddHours(-25);
        await harness.Coordinator.CheckIfDueAsync(Now);
        Assert.Equal(1, handler.Count);
        Assert.Single(harness.Logs);
        Assert.Equal(1, harness.PersistCount);
        Assert.Equal(Now, harness.Settings.LastUpdateCheck);
        Assert.Null(harness.Coordinator.State.Pending);
        Assert.Null(harness.Coordinator.State.Error);
        Assert.Empty(harness.States);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"tag_name\":\"v1.2.2\",\"draft\":true}")]
    [InlineData("{\"tag_name\":\"v1.2.2\",\"prerelease\":true}")]
    public async Task MalformedDraftOrPrereleaseFeedShowsNoUiError(string body)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Ok(body)));
        using var harness = new CoordinatorHarness(handler);
        await harness.Coordinator.CheckIfDueAsync(Now);
        Assert.Equal(1, handler.Count);
        Assert.Single(harness.Logs); // bad JSON/draft/prerelease is logged silently, never surfaced in UI
        Assert.Equal(1, harness.PersistCount);
        Assert.Null(harness.Coordinator.State.Pending);
        Assert.Null(harness.Coordinator.State.Error);
        Assert.Empty(harness.States);
    }

    [Fact]
    public async Task ChecksNeverOverlap()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHandler(async (_, _) =>
        {
            entered.TrySetResult();
            await gate.Task;
            return Ok(FeedJson("v1.2.2"));
        });
        using var harness = new CoordinatorHarness(handler);
        Task first = harness.Coordinator.CheckIfDueAsync(Now);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Coordinator.CheckIfDueAsync(Now.AddHours(25));
        Assert.Equal(1, handler.Count);
        gate.SetResult();
        await first;
        Assert.NotNull(harness.Coordinator.State.Pending);
    }

    [Fact]
    public async Task TurningOffDuringARequestDiscardsTheResponseWithoutUi()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHandler(async (_, _) =>
        {
            entered.TrySetResult();
            await gate.Task;
            return Ok(FeedJson("v1.2.2"));
        });
        using var harness = new CoordinatorHarness(handler);
        Task check = harness.Coordinator.CheckIfDueAsync(Now);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Settings.CheckForUpdates = false;
        gate.SetResult();
        await check;
        Assert.Equal(1, handler.Count);
        Assert.Null(harness.Coordinator.State.Pending);
        Assert.Null(harness.Coordinator.State.Error);
        Assert.Empty(harness.States);
        Assert.Empty(harness.Logs);
    }

    [Fact]
    public async Task StopCancelsAndSuppressesLateState()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHandler(async (_, token) =>
        {
            token.Register(() => cancellation.TrySetResult());
            entered.TrySetResult();
            await gate.Task;
            return Ok(FeedJson("v1.2.2"));
        });
        using var harness = new CoordinatorHarness(handler);
        Task check = harness.Coordinator.CheckIfDueAsync(Now);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Coordinator.Stop();
        await cancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.SetResult();
        await check;
        Assert.Null(harness.Coordinator.State.Pending);
        Assert.Null(harness.Coordinator.State.Error);
        Assert.Empty(harness.States);
        Assert.Empty(harness.Logs);
    }

    [Fact]
    public async Task SwitchingOffCancelsTheDownloadAndLeavesNoInstaller()
    {
        string tempRoot = NewScratch("switch-off-download");
        using var temp = new TempScope(tempRoot);
        try
        {
            var installerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var installerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new FakeHandler(async (request, token) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Ok(Manifest());
                if (path.EndsWith(SetupFile, StringComparison.Ordinal))
                {
                    installerEntered.TrySetResult();
                    await installerGate.Task.WaitAsync(token);
                    return Raw(InstallerBytes);
                }
                return Ok(ReleaseJson());
            });
            using var harness = new CoordinatorHarness(handler);
            await harness.Coordinator.CheckIfDueAsync(Now);
            Assert.NotNull(harness.Coordinator.State.Pending);

            Task install = harness.Coordinator.InstallAsync();
            await installerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            harness.Settings.CheckForUpdates = false;
            harness.Coordinator.PreferenceChanged();
            await install.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, harness.HandoffCount);
            Assert.Null(harness.Coordinator.State.Pending);
            Assert.False(harness.Coordinator.State.Busy);
            Assert.Null(harness.Coordinator.State.Error);
            Assert.Empty(harness.Logs);
            Assert.Empty(Directory.EnumerateFiles(tempRoot, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(tempRoot); }
    }

    [Fact]
    public async Task CancelDownloadPreventsHandoffAndDeletesThePartialFile()
    {
        string tempRoot = NewScratch("cancel-download");
        using var temp = new TempScope(tempRoot);
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new GatedStream([1, 2, 3], started, release)),
                    });
                return Task.FromResult(Ok(ReleaseJson()));
            });
            using var harness = new CoordinatorHarness(handler);
            await harness.Coordinator.CheckIfDueAsync(Now);
            Task install = harness.Coordinator.InstallAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            harness.Coordinator.CancelDownload();
            await install.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, harness.HandoffCount);
            Assert.False(harness.Coordinator.State.Busy);
            Assert.Null(harness.Coordinator.State.Error);
            Assert.Empty(Directory.EnumerateFiles(tempRoot, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(tempRoot); }
    }

    [Fact]
    public async Task SkipPersistsAndClearsThePendingUpdate()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Ok(FeedJson("v1.2.2"))));
        using var harness = new CoordinatorHarness(handler);
        await harness.Coordinator.CheckIfDueAsync(Now);
        Assert.NotNull(harness.Coordinator.State.Pending);
        harness.Coordinator.Skip();
        Assert.Equal("1.2.2", harness.Settings.SkippedUpdateVersion);
        Assert.Equal(2, harness.PersistCount);
        Assert.Null(harness.Coordinator.State.Pending);
        Assert.False(harness.Coordinator.CanInstall);
    }

    [Fact]
    public async Task InvalidRunningVersionNeverChecks()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP for an unparsable version"));
        using var harness = new CoordinatorHarness(handler, runningVersion: "not-a-version");
        await harness.Coordinator.CheckIfDueAsync(Now);
        Assert.Equal(0, handler.Count);
        Assert.Equal(0, harness.PersistCount);
        Assert.Empty(harness.States);
    }

    [Fact]
    public async Task FailedHandoffShowsAnErrorKeepsRunningAndCleansUp()
    {
        string tempRoot = NewScratch("handoff-false");
        using var temp = new TempScope(tempRoot);
        try
        {
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Ok(ReleaseJson()));
            });
            VerifiedInstaller? handedOff = null;
            using var harness = new CoordinatorHarness(handler);
            harness.Handoff = installer => { handedOff = installer; return Task.FromResult(false); };
            await harness.Coordinator.CheckIfDueAsync(Now);
            await harness.Coordinator.InstallAsync();

            Assert.Equal(1, harness.HandoffCount);
            Assert.NotNull(handedOff);
            Assert.Equal("FlowDesk could not start the update. Try again or download it from the releases page.",
                harness.Coordinator.State.Error);
            Assert.NotNull(harness.Coordinator.State.Pending);
            Assert.False(harness.Coordinator.State.Busy);
            Assert.True(harness.Coordinator.CanInstall); // not stopped or exited
            Assert.False(File.Exists(handedOff!.Path));
        }
        finally { TryDeleteTree(tempRoot); }
    }

    // ---- Settings transfer and installer source ---------------------------------------

    [Fact]
    public void UpdateSwitchIsExportedButPerMachineStateIsNot()
    {
        var settings = new AppSettings
        {
            CheckForUpdates = false,
            SkippedUpdateVersion = "1.2.2",
            LastUpdateCheck = Now,
        };
        string json = SettingsTransfer.Export(new(), settings, new(), "test");
        var section = JsonNode.Parse(json)!["sections"]!["settings"]!.AsObject();
        Assert.False(section["CheckForUpdates"]!.GetValue<bool>());
        Assert.False(section.ContainsKey("SkippedUpdateVersion"));
        Assert.False(section.ContainsKey("LastUpdateCheck"));
        Assert.DoesNotContain("\"SkippedUpdateVersion\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LastUpdateCheck\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportKeepsLocalSkippedVersionAndLastCheck()
    {
        var local = new AppSettings
        {
            CheckForUpdates = true,
            SkippedUpdateVersion = "1.2.3",
            LastUpdateCheck = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        };
        var imported = new AppSettings
        {
            CheckForUpdates = false,
            SkippedUpdateVersion = "9.9.9",
            LastUpdateCheck = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        string json = SettingsTransfer.Export(new(), imported, new(), "test");
        TransferPreview preview = SettingsTransfer.Parse(Encoding.UTF8.GetBytes(json));
        Assert.Null(preview.Error);
        TransferPlan plan = SettingsTransfer.Merge(new(), local, preview, new());

        Assert.False(plan.Settings.CheckForUpdates);
        Assert.Equal(local.SkippedUpdateVersion, plan.Settings.SkippedUpdateVersion);
        Assert.Equal(local.LastUpdateCheck, plan.Settings.LastUpdateCheck);
    }

    [Fact]
    public void InstallerHasTheRelaunchSwitchSkipSilentAndPidWait()
    {
        string iss = RepoFile("installer", "FlowDesk.iss");
        Assert.Contains("function RelaunchRequested", iss, StringComparison.Ordinal);
        Assert.Contains("ParamStr(I)", iss, StringComparison.Ordinal);
        Assert.Contains("/RELAUNCH", iss, StringComparison.Ordinal);

        string[] runEntries = iss.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("Filename:", StringComparison.Ordinal))
            .ToArray();
        string normal = Assert.Single(runEntries, line => line.Contains("postinstall", StringComparison.Ordinal));
        Assert.Contains("skipifsilent", normal, StringComparison.Ordinal);
        Assert.DoesNotContain("skipifnotsilent", normal, StringComparison.Ordinal);
        string relaunch = Assert.Single(runEntries, line => line.Contains("skipifnotsilent", StringComparison.Ordinal));
        Assert.Contains("RelaunchRequested", relaunch, StringComparison.Ordinal);

        Assert.Contains("/FLOWDESKPID=", iss, StringComparison.Ordinal);
        Assert.Contains("OpenProcess", iss, StringComparison.Ordinal);
        Assert.Contains("WaitForSingleObject", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugFeedOverrideStaysInsideItsPreprocessorBranches()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", "Services", "UpdateTransport.cs"));

        int firstIf = source.IndexOf("#if DEBUG", StringComparison.Ordinal);
        int firstElse = source.IndexOf("#else", firstIf, StringComparison.Ordinal);
        int firstEnd = source.IndexOf("#endif", firstElse, StringComparison.Ordinal);
        Assert.True(firstIf >= 0 && firstElse > firstIf && firstEnd > firstElse);
        string debugConstant = source[firstIf..firstElse];
        string releaseConstant = source[firstElse..firstEnd];
        Assert.Contains("DebugFeedOverrideEnabled = true", debugConstant, StringComparison.Ordinal);
        Assert.Contains("DebugFeedOverrideEnabled = false", releaseConstant, StringComparison.Ordinal);

        int env = source.IndexOf("FLOWDESK_UPDATE_FEED", StringComparison.Ordinal);
        Assert.True(env >= 0);
        int envIf = source.LastIndexOf("#if DEBUG", env, StringComparison.Ordinal);
        int envEnd = source.IndexOf("#endif", env, StringComparison.Ordinal);
        Assert.True(envIf >= 0 && envEnd > env);
        string envBlock = source[envIf..envEnd];
        Assert.Contains("FLOWDESK_UPDATE_FEED", envBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("FLOWDESK_UPDATE_FEED", source[..envIf], StringComparison.Ordinal);
        Assert.DoesNotContain("FLOWDESK_UPDATE_FEED", source[envEnd..], StringComparison.Ordinal);
    }

    [Fact]
    public void DebugFeedOverrideCompiledValueMatchesTheBuildConfiguration()
    {
#if DEBUG
        Assert.True(UpdateTransport.DebugFeedOverrideEnabled);
#else
        Assert.False(UpdateTransport.DebugFeedOverrideEnabled);
#endif
    }

    // ---- Regression: pinned hand-off, gated coordinator, local override, source order -----

    [Fact]
    public async Task LaunchDelegateCannotWriteDeleteOrRenameWhilePinned()
    {
        string dir = NewScratch("launch-pinned");
        try
        {
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var transport = NewTransport("1.2.0", handler);
            var installer = await transport.PrepareAsync(MakeRelease(), dir, null, CancellationToken.None);
            string path = installer.Path;
            string leaf = Path.GetDirectoryName(path)!;
            string versionFolder = Path.GetDirectoryName(leaf)!;
            string root = Path.GetDirectoryName(versionFolder)!;
            Assert.Equal("FlowDesk-Update", Path.GetFileName(root));
            Assert.Equal(Tag.TrimStart('v'), Path.GetFileName(versionFolder));

            bool delegateRan = false;
            bool launched = installer.Launch(_ =>
            {
                AssertBlocked("write exe", () =>
                {
                    using var _ = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                });
                AssertBlocked("delete exe", () => File.Delete(path));
                // These are real directory renames, not mocks: the pinned ancestors must refuse them.
                AssertBlocked("rename leaf", () => Directory.Move(leaf, leaf + "-moved"));
                AssertBlocked("rename version", () => Directory.Move(versionFolder, versionFolder + "-moved"));
                AssertBlocked("rename root", () => Directory.Move(root, root + "-moved"));
                Assert.Equal(InstallerBytes, File.ReadAllBytes(path));
                Assert.True(Directory.Exists(leaf));
                Assert.True(Directory.Exists(versionFolder));
                Assert.True(Directory.Exists(root));
                delegateRan = true;
                return false; // unsuccessful hand-off: Dispose must clean the download
            });

            Assert.False(launched);
            Assert.True(delegateRan);
            Assert.True(File.Exists(path)); // still pinned until Dispose
            Assert.True(Directory.Exists(leaf));
            installer.Dispose();
            Assert.False(File.Exists(path));
            Assert.False(Directory.Exists(leaf));
            Assert.True(Directory.Exists(versionFolder));
            Assert.Empty(Directory.EnumerateFileSystemEntries(versionFolder));
            Assert.Empty(Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(dir); }
    }

    [Fact]
    public async Task TurningOffDuringGatedHandoffDiscardsPendingErrorAndTheInstaller()
    {
        string tempRoot = NewScratch("handoff-off");
        using var temp = new TempScope(tempRoot);
        try
        {
            var handoffEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handoffGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Ok(ReleaseJson()));
            });
            using var harness = new CoordinatorHarness(handler);
            string? handedOffPath = null;
            harness.Handoff = async installer =>
            {
                handedOffPath = installer.Path;
                handoffEntered.TrySetResult();
                await handoffGate.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return false;
            };
            await harness.Coordinator.CheckIfDueAsync(Now);
            Task install = harness.Coordinator.InstallAsync();
            await handoffEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(handedOffPath);
            Assert.True(File.Exists(handedOffPath!));
            Assert.True(harness.Coordinator.HandoffAllowed);

            harness.Settings.CheckForUpdates = false;
            harness.Coordinator.PreferenceChanged();
            Assert.False(harness.Coordinator.HandoffAllowed);

            handoffGate.SetResult();
            await install.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, harness.HandoffCount);
            Assert.Null(harness.Coordinator.State.Pending);
            Assert.Null(harness.Coordinator.State.Error);
            Assert.False(harness.Coordinator.State.Busy);
            Assert.False(harness.Coordinator.HandoffAllowed);
            Assert.False(File.Exists(handedOffPath!));
            Assert.Empty(Directory.EnumerateFiles(tempRoot, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(tempRoot); }
    }

    [Fact]
    public async Task CancelDownloadDuringGatedHandoffPreventsAnyLaunch()
    {
        string tempRoot = NewScratch("handoff-cancel");
        using var temp = new TempScope(tempRoot);
        try
        {
            var handoffEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handoffGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Ok(ReleaseJson()));
            });
            using var harness = new CoordinatorHarness(handler);
            string? handedOffPath = null;
            bool launched = false;
            harness.Handoff = async installer =>
            {
                handedOffPath = installer.Path;
                handoffEntered.TrySetResult();
                await handoffGate.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // Mirrors MainWindow: launch only while the coordinator still says the hand-off is allowed.
                if (harness.Coordinator.HandoffAllowed && installer.Launch(_ => { launched = true; return true; }))
                    return true;
                return false;
            };
            await harness.Coordinator.CheckIfDueAsync(Now);
            Task install = harness.Coordinator.InstallAsync();
            await handoffEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(harness.Coordinator.HandoffAllowed);

            harness.Coordinator.CancelDownload();
            Assert.False(harness.Coordinator.HandoffAllowed);

            handoffGate.SetResult();
            await install.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, harness.HandoffCount);
            Assert.False(launched);
            Assert.False(harness.Coordinator.HandoffAllowed);
            Assert.Null(harness.Coordinator.State.Error); // cancellation is silent
            Assert.False(File.Exists(handedOffPath!));
            Assert.Empty(Directory.EnumerateFiles(tempRoot, "*.exe", SearchOption.AllDirectories));
        }
        finally { TryDeleteTree(tempRoot); }
    }

    [Fact]
    public async Task DuplicateInstallDuringGatedDownloadDownloadsOnePairAndNoFeed()
    {
        string tempRoot = NewScratch("duplicate-download");
        using var temp = new TempScope(tempRoot);
        try
        {
            var installerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var installerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int feedRequests = 0;
            var handler = new FakeHandler(async (request, token) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (request.RequestUri.AbsoluteUri == UpdateTransport.FeedUrl)
                {
                    Interlocked.Increment(ref feedRequests);
                    return Ok(ReleaseJson());
                }
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Ok(Manifest());
                if (path.EndsWith(SetupFile, StringComparison.Ordinal))
                {
                    installerEntered.TrySetResult();
                    await installerGate.Task.WaitAsync(token);
                    return Raw(InstallerBytes);
                }
                return Status(HttpStatusCode.NotFound);
            });
            using var harness = new CoordinatorHarness(handler);
            int Count(string suffix) =>
                handler.Seen.Count(r => r.Uri.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal));

            await harness.Coordinator.CheckIfDueAsync(Now);
            Assert.Equal(1, feedRequests);
            Task first = harness.Coordinator.InstallAsync();
            await installerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await harness.Coordinator.InstallAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Count(SumsFile));
            Assert.Equal(1, Count(SetupFile));
            Assert.Equal(0, harness.HandoffCount);

            await harness.Coordinator.CheckIfDueAsync(Now.AddHours(25));
            Assert.Equal(1, feedRequests); // a busy coordinator never polls the feed
            Assert.Equal(1, Count(SumsFile));
            Assert.Equal(1, Count(SetupFile));

            installerGate.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, harness.HandoffCount);
            Assert.Equal(1, Count(SumsFile));
            Assert.Equal(1, Count(SetupFile));
        }
        finally { TryDeleteTree(tempRoot); }
    }

    [Fact]
    public async Task DuplicateInstallDuringGatedHandoffDownloadsOnePairAndHandsOffOnce()
    {
        string tempRoot = NewScratch("duplicate-handoff");
        using var temp = new TempScope(tempRoot);
        try
        {
            var handoffEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handoffGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int feedRequests = 0;
            var handler = new FakeHandler((request, _) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (request.RequestUri.AbsoluteUri == UpdateTransport.FeedUrl)
                {
                    Interlocked.Increment(ref feedRequests);
                    return Task.FromResult(Ok(ReleaseJson()));
                }
                if (path.EndsWith(SumsFile, StringComparison.Ordinal)) return Task.FromResult(Ok(Manifest()));
                if (path.EndsWith(SetupFile, StringComparison.Ordinal)) return Task.FromResult(Raw(InstallerBytes));
                return Task.FromResult(Status(HttpStatusCode.NotFound));
            });
            using var harness = new CoordinatorHarness(handler);
            harness.Handoff = async _ =>
            {
                handoffEntered.TrySetResult();
                await handoffGate.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return false;
            };
            int Count(string suffix) =>
                handler.Seen.Count(r => r.Uri.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal));

            await harness.Coordinator.CheckIfDueAsync(Now);
            Task first = harness.Coordinator.InstallAsync();
            await handoffEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await harness.Coordinator.InstallAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Count(SumsFile));
            Assert.Equal(1, Count(SetupFile));
            Assert.Equal(1, harness.HandoffCount);

            await harness.Coordinator.CheckIfDueAsync(Now.AddHours(25));
            Assert.Equal(1, feedRequests); // a busy coordinator never polls the feed

            handoffGate.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, harness.HandoffCount);
            Assert.Equal(1, Count(SumsFile));
            Assert.Equal(1, Count(SetupFile));
        }
        finally { TryDeleteTree(tempRoot); }
    }

    [Fact]
    public void LocalOverrideAllowsOnlyTheSameOriginInDebug()
    {
        const string feed = "http://127.0.0.1:51234/releases/latest";
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("A policy check must not use HTTP"));
        using var transport = NewTransport("1.2.0", handler, feed);
        var same = new Uri("http://127.0.0.1:51234/asset/FlowDesk.exe");
        var wrongPort = new Uri("http://127.0.0.1:51235/asset/FlowDesk.exe");
        var otherName = new Uri("http://localhost:51234/asset/FlowDesk.exe");
        var otherHost = new Uri("http://192.168.1.5:51234/asset/FlowDesk.exe");
#if DEBUG
        Assert.Equal(new Uri(feed), transport.Feed);
        Assert.True(transport.IsAssetAllowed(same, Tag));
        Assert.True(transport.IsRedirectAllowed(same));
        Assert.False(transport.IsAssetAllowed(wrongPort, Tag));
        Assert.False(transport.IsRedirectAllowed(wrongPort));
        Assert.False(transport.IsAssetAllowed(otherName, Tag));
        Assert.False(transport.IsRedirectAllowed(otherName));
        Assert.False(transport.IsAssetAllowed(otherHost, Tag));
#else
        Assert.Equal(new Uri(UpdateTransport.FeedUrl), transport.Feed);
        Assert.False(transport.IsAssetAllowed(same, Tag));
        Assert.False(transport.IsRedirectAllowed(same));
        Assert.False(transport.IsAssetAllowed(wrongPort, Tag));
        Assert.False(transport.IsAssetAllowed(otherName, Tag));
        Assert.False(transport.IsAssetAllowed(otherHost, Tag));
#endif
    }

    [Fact]
    public void LocalhostOverrideAllowsOnlyTheLocalhostOriginInDebug()
    {
        const string feed = "http://localhost:51234/releases/latest";
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("A policy check must not use HTTP"));
        using var transport = NewTransport("1.2.0", handler, feed);
        var same = new Uri("http://localhost:51234/asset/FlowDesk.exe");
        var wrongPort = new Uri("http://localhost:51235/asset/FlowDesk.exe");
        var numeric = new Uri("http://127.0.0.1:51234/asset/FlowDesk.exe");
        var lookalike = new Uri("http://localhost.evil:51234/asset/FlowDesk.exe");
#if DEBUG
        Assert.Equal(new Uri(feed), transport.Feed);
        Assert.True(transport.IsAssetAllowed(same, Tag));
        Assert.True(transport.IsRedirectAllowed(same));
        Assert.False(transport.IsAssetAllowed(wrongPort, Tag));
        Assert.False(transport.IsRedirectAllowed(wrongPort));
        Assert.False(transport.IsAssetAllowed(numeric, Tag));
        Assert.False(transport.IsRedirectAllowed(numeric));
        Assert.False(transport.IsAssetAllowed(lookalike, Tag));
#else
        Assert.Equal(new Uri(UpdateTransport.FeedUrl), transport.Feed);
        Assert.False(transport.IsAssetAllowed(same, Tag));
        Assert.False(transport.IsRedirectAllowed(same));
        Assert.False(transport.IsAssetAllowed(wrongPort, Tag));
        Assert.False(transport.IsAssetAllowed(numeric, Tag));
        Assert.False(transport.IsAssetAllowed(lookalike, Tag));
#endif
    }

    [Fact]
    public void HttpIsRejectedEverywhereWithoutTheEnvironmentOverride()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("A policy check must not use HTTP"));
        using var transport = NewTransport("1.2.0", handler);
        Assert.Equal(new Uri(UpdateTransport.FeedUrl), transport.Feed);
        Assert.False(transport.IsAssetAllowed(new Uri("http://127.0.0.1:51234/x"), Tag));
        Assert.False(transport.IsRedirectAllowed(new Uri("http://127.0.0.1:51234/x")));
        Assert.False(transport.IsAssetAllowed(new Uri("http://localhost:51234/x"), Tag));
        Assert.False(transport.IsRedirectAllowed(new Uri("http://localhost:51234/x")));
    }

    [Fact]
    public void ClosePathFlushesAndLaunchesBeforeStoppingTheUpdater()
    {
        string source = RepoFile("src", "FlowDesk.App", "MainWindow.xaml.cs");

        int flush = source.IndexOf("await services.FlushAsync();", StringComparison.Ordinal);
        Assert.True(flush >= 0, "the normal close path must flush pending saves");
        int saveCheck = source.IndexOf("if (!services.LastSaveSucceeded)", flush, StringComparison.Ordinal);
        Assert.True(saveCheck > flush, "FlushAsync success must be checked before launching setup");
        int launch = source.IndexOf("updateInstaller.Launch()", saveCheck, StringComparison.Ordinal);
        Assert.True(launch > saveCheck, "the installer may only launch after a successful save");
        int stop = source.IndexOf("updater?.Stop();", launch, StringComparison.Ordinal);
        Assert.True(stop > launch, "updater.Stop must only run after the installer hand-off");
        int cleanup = source.IndexOf("services.HotCorners?.Dispose()", stop, StringComparison.Ordinal);
        Assert.True(cleanup > stop, "hook/tray cleanup must follow updater.Stop");

        int session = source.IndexOf("private void OnSessionEnd()", StringComparison.Ordinal);
        Assert.True(session >= 0, "OnSessionEnd must exist");
        int sessionStop = source.IndexOf("updater?.Stop();", session, StringComparison.Ordinal);
        Assert.True(sessionStop > session, "session end must cancel the updater");
        int sessionFlush = source.IndexOf("services.FlushForSessionEnd();", session, StringComparison.Ordinal);
        Assert.True(sessionFlush > sessionStop, "session end must cancel the updater before the final flush");
    }

    [Fact]
    public void ImportAndUndoRaiseSettingsReplacedAndTheUpdaterListens()
    {
        string lifetime = RepoFile("src", "FlowDesk.App", "MainWindow.UpdateLifetime.cs");
        Assert.Contains("App.ViewModels.Settings.UpdatePreferenceChanged += updater.PreferenceChanged;",
            lifetime, StringComparison.Ordinal);
        Assert.Contains("services.SettingsReplaced += updater.PreferenceChanged;",
            lifetime, StringComparison.Ordinal);

        string services = RepoFile("src", "FlowDesk.App", "Services", "AppServices.cs");
        int import = services.IndexOf("internal async Task<ImportUndo> ImportAsync", StringComparison.Ordinal);
        Assert.True(import >= 0, "ImportAsync must exist");
        int undo = services.IndexOf("internal async Task UndoImportAsync", import, StringComparison.Ordinal);
        Assert.True(undo > import, "UndoImportAsync must follow ImportAsync");
        Assert.Contains("SettingsReplaced?.Invoke();", services[import..undo], StringComparison.Ordinal);
        Assert.Contains("SettingsReplaced?.Invoke();", services[undo..], StringComparison.Ordinal);

        string coordinator = RepoFile("src", "FlowDesk.App", "Services", "UpdateCoordinator.cs");
        int preference = coordinator.IndexOf("public void PreferenceChanged()", StringComparison.Ordinal);
        Assert.True(preference >= 0, "PreferenceChanged must exist");
        int skip = coordinator.IndexOf("public void Skip()", preference, StringComparison.Ordinal);
        string block = coordinator[preference..skip];
        Assert.Contains("operation?.Cancel();", block, StringComparison.Ordinal);
        Assert.Contains("SetState(new(null, State.Busy, null, null));", block, StringComparison.Ordinal);
    }

    // ---- Helpers ----------------------------------------------------------------------

    private static string AssetUrl(string tag, string file) =>
        "https://github.com/kiraa000/FlowDesk/releases/download/" + tag + "/" + file;

    private static UpdateRelease MakeRelease()
    {
        Assert.True(UpdateVersion.TryParse(Tag, out UpdateVersion? version));
        return new UpdateRelease(version!, Tag, new Uri("https://github.com/kiraa000/FlowDesk/releases/tag/" + Tag),
            new Uri(AssetUrl(Tag, SetupFile)), new Uri(AssetUrl(Tag, SumsFile)));
    }

    private static string Manifest() => Sha(InstallerBytes) + "  " + SetupFile + "\n";

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static string FeedJson(string tag) =>
        "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false}";

    private static string ReleaseJson(string tag = Tag)
    {
        string version = tag.StartsWith('v') ? tag[1..] : tag;
        string setup = "FlowDesk-" + version + "-win-x64-setup.exe";
        return "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"html_url\":\""
            + "https://github.com/kiraa000/FlowDesk/releases/tag/" + tag + "\",\"assets\":["
            + "{\"name\":\"" + setup + "\",\"browser_download_url\":\"" + AssetUrl(tag, setup) + "\"},"
            + "{\"name\":\"" + SumsFile + "\",\"browser_download_url\":\"" + AssetUrl(tag, SumsFile) + "\"}]}";
    }

    private static UpdateTransport NewTransport(string version, HttpMessageHandler handler, string? feedOverride = null)
    {
        string? old = Environment.GetEnvironmentVariable("FLOWDESK_UPDATE_FEED");
        try
        {
            Environment.SetEnvironmentVariable("FLOWDESK_UPDATE_FEED", feedOverride);
            return new UpdateTransport(version, handler);
        }
        finally { Environment.SetEnvironmentVariable("FLOWDESK_UPDATE_FEED", old); }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Raw(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Status(HttpStatusCode status) =>
        new(status) { Content = new StringContent("") };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private static HttpResponseMessage DeclaredLength(long length)
    {
        var content = new StreamContent(Stream.Null);
        content.Headers.ContentLength = length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpContent Unknown(byte[] body) => new StreamContent(new NonSeekableStream(body));

    private static string NewScratch(string name)
    {
        string dir = Path.Combine(ScratchRoot, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    private static void AssertBlocked(string action, Action attempt)
    {
        Exception? exception = Record.Exception(attempt);
        Assert.NotNull(exception);
        Assert.True(exception is IOException or UnauthorizedAccessException,
            action + " failed with an unexpected " + exception!.GetType().Name);
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "installer", "FlowDesk.iss")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    private sealed record SeenRequest(string Method, Uri Uri, string Accept, string UserAgent);

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly List<SeenRequest> seen = [];
        public IReadOnlyList<SeenRequest> Seen { get { lock (seen) return seen.ToArray(); } }
        public int Count { get { lock (seen) return seen.Count; } }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (seen)
            {
                seen.Add(new SeenRequest(request.Method.Method, request.RequestUri!,
                    request.Headers.Accept.ToString(), request.Headers.UserAgent.ToString()));
            }
            return await responder(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class CoordinatorHarness : IDisposable
    {
        public AppSettings Settings { get; }
        public FakeHandler Handler { get; }
        public List<Exception> Logs { get; } = [];
        public List<UpdateState> States { get; } = [];
        public int PersistCount { get; private set; }
        public int HandoffCount { get; private set; }
        public Func<VerifiedInstaller, Task<bool>> Handoff { get; set; } = _ => Task.FromResult(true);
        public UpdateTransport Transport { get; }
        public UpdateCoordinator Coordinator { get; }

        public CoordinatorHarness(FakeHandler handler, AppSettings? settings = null, string runningVersion = "1.2.0")
        {
            Handler = handler;
            Settings = settings ?? new AppSettings();
            Transport = NewTransport(runningVersion, handler);
            Coordinator = new UpdateCoordinator(Transport, runningVersion, () => Settings, () => PersistCount++,
                exception => Logs.Add(exception),
                installer => { HandoffCount++; return Handoff(installer); });
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

    private class NonSeekableStream(byte[] data) : Stream
    {
        private int position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(buffer.Length, data.Length - position);
            data.AsSpan(position, count).CopyTo(buffer);
            position += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<int>(Read(buffer.Span));
        }
    }

    private sealed class GatedStream(
        byte[] prefix, TaskCompletionSource started, TaskCompletionSource release) : Stream
    {
        private int position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position < prefix.Length)
            {
                int count = Math.Min(buffer.Length, prefix.Length - position);
                prefix.AsSpan(position, count).CopyTo(buffer.Span);
                position += count;
                return count;
            }
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return 0;
        }
    }
}
