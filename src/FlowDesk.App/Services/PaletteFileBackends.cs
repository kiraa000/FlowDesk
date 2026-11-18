using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Services;

internal static class PaletteFileBackends
{
    public static async Task<IPaletteFileBackend?> DetectAsync()
    {
        // Called once per palette open, away from the dispatcher. No service start, disk walk or install.
        nint everything = EverythingFileBackend.Find();
        if (everything != 0) return new EverythingFileBackend(everything);
        return await WindowsSearchFileBackend.AvailableAsync().ConfigureAwait(false) ? new WindowsSearchFileBackend() : null;
    }

    internal static async Task<T> WithLeaseAsync<T>(SemaphoreSlim gate, Func<T> read, CancellationToken token)
    {
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false)) throw new TimeoutException("File search is busy.");
        var work = Task.Run(read);
        // A cancelled caller must not release a lease while COM/native work is still running.
        _ = work.ContinueWith(t => { _ = t.Exception; gate.Release(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await work.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
    }
}

internal sealed class WindowsSearchFileBackend : IPaletteFileBackend
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public string Name => "Windows Search · User profile";
    public static async Task<bool> AvailableAsync()
    {
        try
        {
            return await PaletteFileBackends.WithLeaseAsync(Gate, () =>
            {
                object? connection = null, rows = null;
                try
                {
                    connection = Connect();
                    // Opening the provider alone succeeds even with WSearch stopped. A bounded
                    // read-only query distinguishes an actually usable index from that false positive.
                    rows = ((dynamic)connection).Execute("SELECT TOP 1 System.ItemPathDisplay FROM SystemIndex");
                    return true;
                }
                finally { Close(rows); Close(connection); }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) { return false; }
    }
    public Task<IReadOnlyList<PaletteFile>> SearchAsync(string query, int limit, CancellationToken token)
        => PaletteFileBackends.WithLeaseAsync<IReadOnlyList<PaletteFile>>(Gate, () =>
        {
            object? connection = null, rows = null;
            try
            {
                token.ThrowIfCancellationRequested();
                connection = Connect();
                string sql = FileSearchQuery.WindowsSearch(query, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), limit);
                rows = ((dynamic)connection).Execute(sql);
                var result = new List<PaletteFile>();
                while (!((dynamic)rows).EOF && result.Count < Math.Clamp(limit, 1, 80))
                {
                    token.ThrowIfCancellationRequested();
                    object? fields = null, pathField = null, typeField = null;
                    try
                    {
                        fields = ((dynamic)rows).Fields;
                        pathField = ((dynamic)fields).Item(0); typeField = ((dynamic)fields).Item(1);
                        string? path = ((dynamic)pathField).Value as string;
                        object attributes = ((dynamic)typeField).Value;
                        if (!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && attributes is not DBNull)
                            result.Add(PaletteFile.FromWindowsSearch(path, Convert.ToUInt32(attributes, System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    finally { Release(typeField); Release(pathField); Release(fields); }
                    ((dynamic)rows).MoveNext();
                }
                return result;
            }
            finally { Close(rows); Close(connection); }
        }, token);
    private static object Connect()
    {
        object connection = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Connection") ?? throw new InvalidOperationException("Windows Search is unavailable."))!;
        try
        {
            ((dynamic)connection).ConnectionTimeout = 2;
            ((dynamic)connection).CommandTimeout = 2;
            ((dynamic)connection).Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            return connection;
        }
        catch { Release(connection); throw; }
    }
    private static void Close(object? value)
    {
        if (value is null) return;
        try { ((dynamic)value).Close(); } catch (Exception) { }
        finally { Release(value); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}

/// <summary>Documented Everything IPC v1 Unicode queries, with a private message-only reply window.
/// Wire definitions: voidtools Everything SDK ipc/everything_ipc.h, QUERYW and LISTW. Never loads a DLL.</summary>
internal sealed class EverythingFileBackend(nint target) : IPaletteFileBackend
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentDictionary<nint, Reply> Replies = new();
    private static readonly WindowProc Callback = OnMessage;
    private static readonly Lazy<ushort> WindowClass = new(() =>
    {
        var wc = new WndClass { Proc = Marshal.GetFunctionPointerForDelegate(Callback), Instance = GetModuleHandleW(null), ClassName = "FlowDesk.FileSearchReply" };
        ushort atom = RegisterClassW(ref wc);
        if (atom == 0) throw new InvalidOperationException("File search reply window is unavailable.");
        return atom;
    });
    private static int nextReply;
    private sealed class Reply(uint id, int limit)
    {
        public uint Id => id;
        public int Limit => limit;
        public IReadOnlyList<PaletteFile>? Results;
        public Exception? Error;
    }
    public string Name => "Everything";
    public static nint Find() => FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null);
    public Task<IReadOnlyList<PaletteFile>> SearchAsync(string query, int limit, CancellationToken token)
        => PaletteFileBackends.WithLeaseAsync(Gate, () => Read(query, limit, token), token);
    private IReadOnlyList<PaletteFile> Read(string query, int limit, CancellationToken token)
    {
        _ = WindowClass.Value;
        nint hwnd = CreateWindowExW(0, "FlowDesk.FileSearchReply", "", 0, 0, 0, 0, 0, -3, 0, GetModuleHandleW(null), 0);
        if (hwnd == 0) throw new InvalidOperationException("File search reply window is unavailable.");
        nint buffer = 0;
        try
        {
            uint id = unchecked((uint)Interlocked.Increment(ref nextReply));
            var reply = new Reply(id, limit); Replies[hwnd] = reply;
            byte[] bytes = EverythingIpcWire.Query(unchecked((uint)(long)hwnd), id, query, limit);
            buffer = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var data = new CopyData { Id = 2, Size = bytes.Length, Data = buffer };
            token.ThrowIfCancellationRequested();
            if (SendMessageTimeoutW(target, 0x004A, hwnd, ref data, 2, 200, out nint accepted) == 0 || accepted == 0)
                throw new InvalidOperationException("Everything could not accept the file search.");
            var clock = Stopwatch.StartNew();
            while (reply.Results is null && reply.Error is null && clock.ElapsedMilliseconds < 2000)
            {
                token.ThrowIfCancellationRequested();
                while (PeekMessageW(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessageW(ref message); }
                if (reply.Results is null && reply.Error is null) Thread.Sleep(5);
            }
            token.ThrowIfCancellationRequested();
            if (reply.Error is not null) throw reply.Error;
            return reply.Results ?? throw new TimeoutException("Everything did not reply to the file search.");
        }
        finally { Replies.TryRemove(hwnd, out _); if (buffer != 0) Marshal.FreeHGlobal(buffer); DestroyWindow(hwnd); }
    }
    private static nint OnMessage(nint hwnd, uint message, nint sender, nint pointer)
    {
        // The documented reply contract identifies responses by dwData, not the sender HWND.
        if (message == 0x004A && Replies.TryGetValue(hwnd, out var reply))
        {
            try
            {
                var data = Marshal.PtrToStructure<CopyData>(pointer);
                if (data.Id != reply.Id || data.Size < 28 || data.Size > 1024 * 1024 || data.Data == 0) return 0;
                byte[] bytes = new byte[data.Size]; Marshal.Copy(data.Data, bytes, 0, bytes.Length);
                reply.Results = EverythingIpcWire.Results(bytes, reply.Limit);
            }
            catch (Exception ex) { reply.Error = ex; }
            return 1;
        }
        return DefWindowProcW(hwnd, message, sender, pointer);
    }
    private delegate nint WindowProc(nint hwnd, uint message, nint sender, nint pointer);
    [StructLayout(LayoutKind.Sequential)] private struct CopyData { public nuint Id; public int Size; public nint Data; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WndClass
    {
        public uint Style; public nint Proc; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? Menu; public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string? title);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WndClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeoutW(nint hwnd, uint message, nint sender, ref CopyData data, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
}
