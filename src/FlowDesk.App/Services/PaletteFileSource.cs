using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Services;

internal sealed class PaletteFileSource(PaletteUsageStore usage, Func<string, Task> copyPath,
    Action<string> copyFile, Func<Task> advanced, Func<nint> owner, Func<string, Task> preview,
    Func<Task<IPaletteFileBackend?>>? detect = null) : IAsyncCommandSource
{
    private Task<IPaletteFileBackend?> backend = Task.FromResult<IPaletteFileBackend?>(null);
    private volatile bool enabled;
    public string Name => "Files";
    public bool Supports(PaletteMode mode) => enabled && mode is PaletteMode.Commands or PaletteMode.Files;
    public Task<IPaletteFileBackend?> BeginOpen(bool value)
    {
        enabled = value;
        var next = value ? Task.Run(detect ?? PaletteFileBackends.DetectAsync) : Task.FromResult<IPaletteFileBackend?>(null);
        Volatile.Write(ref backend, next);
        return next;
    }
    public async Task<IReadOnlyList<CommandMatch>> QueryAsync(string query, PaletteMode mode, CancellationToken token)
    {
        if (!Supports(mode)) return [];
        var detected = await Volatile.Read(ref backend).WaitAsync(token).ConfigureAwait(false);
        if (detected is null) return mode == PaletteMode.Files ? [Hint("File search needs Everything or Windows Search")] : [];
        if (query.Trim().Length == 0) return [];
        try
        {
            var files = await detected.SearchAsync(query.Trim(), mode == PaletteMode.Files ? 80 : 20, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return files.Select(file => new CommandMatch(Item(file) with { IsFileSearch = true }, CommandMatcher.Match(file.Name, query, false).Score + usage.Boost(file.Id), [])).ToArray();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return mode == PaletteMode.Files ? [Hint("File search is unavailable · Open Advanced to check Everything")] : []; }
    }
    private CommandMatch Hint(string text) => new(new("file-search:setup", text, "Open Advanced", CommandKind.Help,
        "\uE8B7", advanced, RecordUsage: false), -500, []);
    public CommandItem Item(PaletteFile file)
    {
        string path = file.Path;
        Func<Task> open = () => PaletteShellIndex.LaunchAsync(path);
        var actions = new List<PaletteAction> { new("Open", open, "\uE8E5", "Enter") };
        if (file.IsFolder) actions.Add(new("Open in Terminal", () => TerminalAsync(path), "\uE756", "Ctrl+Enter"));
        else
        {
            actions.Add(new("Show in Explorer", () => PaletteShellIndex.OpenLocationAsync(path), "\uE838", "Ctrl+Enter"));
            actions.Add(new("Open with…", () => OpenWithAsync(path), "\uE8A7"));
            actions.Add(new("Copy file", () => { copyFile(path); return Task.CompletedTask; }, "\uE8C8"));
            actions.Add(new("Quick Look", () => preview(path), "\uE890", "Ctrl+Y", KeepsPaletteOpen: true));
            if (FileSearchQuery.CanElevate(path)) actions.Add(new("Run as administrator", () => ElevateAsync(path), "\uEA18"));
        }
        actions.Add(new("Copy path", () => copyPath(path), "\uE8C8"));
        return new(file.Id, file.Name, FileSearchQuery.MiddleEllipsis(Path.GetDirectoryName(path) ?? path), CommandKind.File,
            path, open, Synonyms: path, NamedActions: actions.AsReadOnly());
    }
    private Task OpenWithAsync(string path)
    {
        var info = new OpenWithInfo { File = path, Flags = 4 }; // OAIF_EXEC only: no forced association change
        int hr = SHOpenWithDialog(owner(), ref info);
        if (hr < 0 && hr != unchecked((int)0x800704C7)) Marshal.ThrowExceptionForHR(hr);
        return Task.CompletedTask;
    }
    private static Task TerminalAsync(string path)
    {
        try
        {
            var start = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            start.ArgumentList.Add("-d"); start.ArgumentList.Add(path); Process.Start(start);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        { Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = path }); }
        return Task.CompletedTask;
    }
    private static Task ElevateAsync(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" }); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        return Task.CompletedTask;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OpenWithInfo
    { public string File; public string? Class; public uint Flags; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHOpenWithDialog(nint owner, ref OpenWithInfo info);
}
