using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Services;

/// <summary>All Shell RCWs live and die on one background STA. Refresh is coalesced, never awaited by search.
/// A hung Shell extension keeps one lease, not an ever-growing collection of replacement threads.</summary>
internal sealed class PaletteShellIndex : IDisposable
{
    public CommandIndex Apps { get; } = new();
    public CommandIndex Recent { get; } = new();
    private readonly BlockingCollection<Action> queue = new(2);
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly Timer timer;
    private readonly CrashLog log = new();
    private int disposed;
    public PaletteShellIndex()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "FlowDesk palette Shell index" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.Recent) })
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName };
                watcher.Changed += (_, _) => Refresh(); watcher.Created += (_, _) => Refresh();
                watcher.Deleted += (_, _) => Refresh(); watcher.Renamed += (_, _) => Refresh();
                watcher.EnableRaisingEvents = true; watchers.Add(watcher);
            }
            catch (Exception ex) { Log(ex); }
        }
    }
    private void Refresh()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        _ = RefreshSafeAsync(Apps, ReadApps);
        _ = RefreshSafeAsync(Recent, ReadRecent);
    }
    private async Task RefreshSafeAsync(CommandIndex index, Func<IReadOnlyList<CommandItem>> read)
    {
        try
        {
            await index.RefreshAsync(() =>
            {
                var done = new TaskCompletionSource<IReadOnlyList<CommandItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (Volatile.Read(ref disposed) != 0 || !queue.TryAdd(() =>
                {
                    if (Volatile.Read(ref disposed) != 0) { done.TrySetCanceled(); return; }
                    try { done.TrySetResult(read()); } catch (Exception ex) { done.TrySetException(ex); }
                })) done.TrySetException(new InvalidOperationException("Shell index unavailable."));
                return done.Task;
            });
        }
        catch (Exception ex) { Log(ex); }
    }
    private void Run()
    {
        OleInitialize(0);
        try { foreach (var work in queue.GetConsumingEnumerable()) work(); }
        finally { OleUninitialize(); }
    }
    private IReadOnlyList<CommandItem> ReadApps()
    {
        object? shell = null, folder = null, entries = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder is null) throw new InvalidOperationException("AppsFolder unavailable.");
            entries = ((dynamic)folder).Items();
            var result = new List<CommandItem>();
            int count = ((dynamic)entries).Count;
            for (int i = 0; i < count; i++)
            {
                object? entry = null;
                try
                {
                    entry = ((dynamic)entries).Item(i);
                    string name = ((dynamic)entry).Name;
                    string? path = ((dynamic)entry).Path as string;
                    string? aumid = Property(entry, "System.AppUserModel.ID");
                    string id = !string.IsNullOrWhiteSpace(aumid) ? aumid : path ?? "";
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                    string identity = !string.IsNullOrWhiteSpace(aumid) ? "shell:AppsFolder\\" + aumid :
                        Path.IsPathFullyQualified(id) || id.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? id : "shell:AppsFolder\\" + id;
                    string? target = Property(entry, "System.Link.TargetParsingPath");
                    if (string.IsNullOrWhiteSpace(target) && Path.IsPathFullyQualified(id)) target = id;
                    // Packaged apps need not expose an accessible executable. Do not label the virtual
                    // AppsFolder root as a file location: secondary exists only for real file targets.
                    Func<Task>? secondary = !string.IsNullOrWhiteSpace(target) && Path.IsPathFullyQualified(target) ? () => OpenLocationAsync(target) : null;
                    result.Add(new("app:" + id, name, "App", CommandKind.App, identity, () => LaunchAsync(identity), secondary));
                }
                catch (Exception ex) { Log(ex); }
                finally { Release(entry); }
            }
            return result;
        }
        finally { Release(entries); Release(folder); Release(shell); }
    }
    private IReadOnlyList<CommandItem> ReadRecent()
    {
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            string? Resolve(string path)
            {
                object? link = null;
                try { link = ((dynamic)shell!).CreateShortcut(path); return ((dynamic)link).TargetPath as string; }
                finally { Release(link); }
            }
            return RecentFileCommandSource.Resolve(Directory.EnumerateFiles(folder, "*.lnk").Take(512), Resolve,
                p => File.Exists(p) || Directory.Exists(p), LaunchAsync, OpenLocationAsync, Log);
        }
        finally { Release(shell); }
    }
    public static Task LaunchAsync(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return Task.CompletedTask;
    }
    private string? Property(object entry, string name)
    {
        try { return ((dynamic)entry).ExtendedProperty(name) as string; }
        catch (Exception ex) { Log(ex); return null; }
    }
    public static Task OpenLocationAsync(string target)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add("/select," + target); Process.Start(start); return Task.CompletedTask;
    }
    private void Log(Exception ex) => log.TryWrite(CrashSource.Activation, ex);
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        timer.Dispose(); foreach (var watcher in watchers) watcher.Dispose();
        queue.CompleteAdding();
        // Do not dispose the bounded queue under a racing watcher or join a stuck Shell call on UI shutdown.
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
}
