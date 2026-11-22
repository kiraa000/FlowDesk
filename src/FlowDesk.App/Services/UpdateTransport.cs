using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FlowDesk.Core.Updates;
using Microsoft.Win32.SafeHandles;

namespace FlowDesk.App.Services;

/// <summary>Bounded, unauthenticated update traffic. Redirects are never delegated to HttpClient.</summary>
public sealed class UpdateTransport : IDisposable
{
    public const int FeedLimit = 1024 * 1024, ChecksumLimit = 64 * 1024, InstallerLimit = 300 * 1024 * 1024;
    public const string FeedUrl = "https://api.github.com/repos/kiraa000/FlowDesk/releases/latest";
#if DEBUG
    public const bool DebugFeedOverrideEnabled = true;
#else
    public const bool DebugFeedOverrideEnabled = false;
#endif
    private readonly HttpClient client;
    private readonly string productVersion;
    public Uri Feed { get; }
    private readonly Uri? localOrigin = null;

    public UpdateTransport(string version, HttpMessageHandler? handler = null)
    {
        productVersion = version;
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None });
        client.Timeout = Timeout.InfiniteTimeSpan; // Each complete operation has a cancellation deadline.
        Feed = new(FeedUrl);
#if DEBUG
        var value = Environment.GetEnvironmentVariable("FLOWDESK_UPDATE_FEED");
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            string.IsNullOrEmpty(candidate.UserInfo) && string.IsNullOrEmpty(candidate.Fragment) &&
            (candidate.Scheme == "https" || IsLocalHttp(candidate)))
        {
            Feed = candidate;
            if (IsLocalHttp(candidate)) localOrigin = candidate;
        }
#endif
    }

    private static bool IsLocalHttp(Uri uri) => uri.Scheme == "http" &&
        (uri.Host == "127.0.0.1" || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    private bool SameLocalOrigin(Uri uri) => localOrigin is not null && IsLocalHttp(uri) &&
        uri.Host.Equals(localOrigin.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == localOrigin.Port;
    private static bool Clean(Uri uri) => uri.IsAbsoluteUri && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);

    public bool IsAssetAllowed(Uri? uri, string tag)
    {
        if (uri is null || !Clean(uri)) return false;
        if (SameLocalOrigin(uri)) return true;
        return uri.Scheme == "https" && uri.IsDefaultPort && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith("/kiraa000/FlowDesk/releases/download/" + Uri.EscapeDataString(tag) + "/", StringComparison.Ordinal);
    }

    public bool IsRedirectAllowed(Uri uri) => Clean(uri) && (SameLocalOrigin(uri) ||
        uri.Scheme == "https" && uri.IsDefaultPort &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)));

    public async Task<UpdateRelease?> CheckAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var output = new MemoryStream();
        await DownloadAsync(Feed, output, FeedLimit, null, deadline.Token, feed: true).ConfigureAwait(false);
        return UpdateRelease.Parse(Encoding.UTF8.GetString(output.ToArray()));
    }

    private async Task DownloadAsync(Uri initial, Stream output, long limit, IProgress<double?>? progress,
        CancellationToken token, bool feed = false)
    {
        Uri current = initial;
        for (int redirects = 0; ; redirects++)
        {
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("FlowDesk-Updater/" + productVersion);
            if (feed) request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status is 301 or 302 or 303 or 307 or 308)
            {
                if (redirects >= 5 || response.Headers.Location is not { } location)
                    throw new InvalidDataException("Update redirect limit");
                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                // Feed redirects are not needed in production; never send its headers to a new origin.
                if (feed ? !(SameLocalOrigin(next) && localOrigin is not null) : !IsRedirectAllowed(next))
                    throw new InvalidDataException("Update redirect origin");
                current = next;
                continue;
            }
            response.EnsureSuccessStatusCode();
            long? length = response.Content.Headers.ContentLength;
            if (length is < 0 || length > limit) throw new InvalidDataException("Update content length");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                int count = await input.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0) break;
                total += count;
                if (total > limit) throw new InvalidDataException("Update size limit");
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                progress?.Report(length is > 0 ? total * 100d / length.Value : null);
            }
            if (length.HasValue && total != length.Value) throw new InvalidDataException("Incomplete update");
            return;
        }
    }

    public async Task<VerifiedInstaller> PrepareAsync(UpdateRelease release, string tempPath,
        IProgress<double?>? progress, CancellationToken token)
    {
        if (!IsAssetAllowed(release.Installer, release.Tag) || !IsAssetAllowed(release.Checksums, release.Tag))
            throw new UpdateVerificationException("This update cannot be checked. Download it from the releases page.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));
        using var sums = new MemoryStream();
        await DownloadAsync(release.Checksums!, sums, ChecksumLimit, null, deadline.Token).ConfigureAwait(false);
        string fileName = $"FlowDesk-{release.Version}-win-x64-setup.exe";
        string? expected = UpdateChecksums.Find(Encoding.UTF8.GetString(sums.ToArray()), fileName);
        if (expected is null) throw new UpdateVerificationException("This update cannot be checked. Download it from the releases page.");

        var locks = new List<SafeFileHandle>();
        string? folder = null;
        FileStream? verified = null;
        try
        {
            // Pin the directory chain against rename/delete as well as rejecting junctions. A file lock
            // alone would not prevent an ancestor from being renamed and replaced before CreateProcess.
            locks.Add(LockDirectory(Path.GetFullPath(tempPath)));
            string root = Path.Combine(tempPath, "FlowDesk-Update");
            Directory.CreateDirectory(root);
            locks.Add(LockDirectory(root));
            string versionFolder = Path.Combine(root, release.Version.ToString());
            Directory.CreateDirectory(versionFolder);
            locks.Add(LockDirectory(versionFolder));
            // Never reuse a previous installer path; leftovers are inert and pruned without following links.
            PruneOldDownloads(root, versionFolder);
            folder = Path.Combine(versionFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            locks.Add(LockDirectory(folder));
            string path = Path.Combine(folder, fileName);
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
                await DownloadAsync(release.Installer!, destination, InstallerLimit, progress, deadline.Token).ConfigureAwait(false);
            verified = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
            var hash = await SHA256.HashDataAsync(verified, deadline.Token).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(expected)))
                throw new UpdateVerificationException("The download did not match its checksum and was deleted.");
            deadline.Token.ThrowIfCancellationRequested();
            var result = new VerifiedInstaller(path, verified, locks, folder);
            verified = null;
            locks = [];
            folder = null;
            return result;
        }
        finally
        {
            verified?.Dispose();
            if (folder is not null && locks.Count == 4)
            {
                locks[^1].Dispose(); // allow deleting only the leaf, while its ancestors remain pinned
                TryDeleteDownload(folder);
            }
            foreach (var handle in locks) handle.Dispose();
        }
    }

    private static SafeFileHandle LockDirectory(string path)
    {
        var handle = CreateFileW(path, 0x80, 3, 0, 3, 0x02200000, 0); // attributes; share R/W, NOT DELETE; backup + open-reparse
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot lock update directory"); }
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0)
        { handle.Dispose(); throw new IOException("Unsafe update directory"); }
        return handle;
    }

    private static void PruneOldDownloads(string root, string current)
    {
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            if (path == current || !UpdateVersion.TryParse(Path.GetFileName(path), out _)) continue;
            TryDeleteDownload(path);
        }
        foreach (var path in Directory.EnumerateDirectories(current))
            if (Guid.TryParseExact(Path.GetFileName(path), "N", out _)) TryDeleteDownload(path);
    }
    internal static void TryDeleteDownload(string folder)
    {
        try
        {
            // Pin each directory BEFORE enumerating it, closing the attributes-check/recursion race.
            using (var pin = LockDirectory(folder))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    var attrs = File.GetAttributes(entry);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) return;
                    if ((attrs & FileAttributes.Directory) != 0) TryDeleteDownload(entry);
                    else File.Delete(entry); // deleting a file link deletes the link, not its target
                }
            }
            // Nonrecursive deletion cannot traverse a late replacement junction.
            Directory.Delete(folder);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose() => client.Dispose();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
}

public sealed class UpdateVerificationException(string message) : Exception(message);

/// <summary>Owns the no-writers/no-delete lock from hashing through CreateProcess returning.</summary>
public sealed class VerifiedInstaller(string path, FileStream file, List<SafeFileHandle> directories, string folder) : IDisposable
{
    private bool launched, disposed;
    public string Path { get; } = path;
    public bool Launch(Func<ProcessStartInfo, bool>? start = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (launched) throw new InvalidOperationException("Installer already launched");
        var info = new ProcessStartInfo(Path) { UseShellExecute = false, WorkingDirectory = System.IO.Path.GetDirectoryName(Path)! };
        foreach (string arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/RELAUNCH", $"/FLOWDESKPID={Environment.ProcessId}" })
            info.ArgumentList.Add(arg);
        if (start is not null) launched = start(info);
        else { using var process = Process.Start(info); launched = process is not null; }
        return launched;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        file.Dispose();
        directories[^1].Dispose();
        if (!launched) UpdateTransport.TryDeleteDownload(folder);
        foreach (var handle in directories) handle.Dispose();
    }
}
