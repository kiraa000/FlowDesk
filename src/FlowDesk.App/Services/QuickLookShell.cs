using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FlowDesk.Core.QuickLook;

namespace FlowDesk.App.Services;

internal sealed record QuickLookItem(string Identity, string Name, string? Path, bool IsFolder, string Type)
{
    public static QuickLookItem Empty { get; } = new("", "", null, false, "");
}

/// <summary>One dedicated background STA and one outstanding operation maximum. A COM server cannot
/// safely be killed in-process: timeout abandons the result, NOT the thread. The busy circuit refuses
/// further work until that call returns; repeated Space never accumulates hung threads/RCWs.</summary>
internal sealed class QuickLookShell : IDisposable
{
    private readonly BlockingCollection<Action> work = new(1);
    private int disposed;
    private readonly SingleFlightPreviewQuery queries = new();
    // Acquired/released exclusively on our STA. ShellWindows remains a live collection; cache only
    // its root, never a tab's Document, which can change while the top-level HWND stays the same.
    private object? application, shellWindows;
    public QuickLookShell()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "FlowDesk Quick Look Shell STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
    public Task<QuickLookItem?> ReadAsync(nint owner, CancellationToken token) => RequestAsync(() => Read(owner), token);
    public Task<byte[]?> ThumbnailAsync(string path, CancellationToken token) => RequestAsync(() => Thumbnail(path), token);
    private Task<T?> RequestAsync<T>(Func<T?> operation, CancellationToken token) where T : class =>
        queries.RunAsync(operation, action => Volatile.Read(ref disposed) == 0 && work.TryAdd(action),
            TimeSpan.FromMilliseconds(700), token);
    private static byte[]? Thumbnail(string path)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        Guid iid = typeof(IShellItemImageFactory).GUID;
        object? factory = null;
        nint bitmap = 0;
        try
        {
            if (SHCreateItemFromParsingName(path, 0, ref iid, out factory) < 0 || factory is null) return null;
            if (deadline.ElapsedMilliseconds >= 650) return null;
            ((IShellItemImageFactory)factory).GetImage(new Size { Width = 512, Height = 512 }, 1, out bitmap);
            if (bitmap == 0) return null;
            using var image = System.Drawing.Image.FromHbitmap(bitmap);
            using var stream = new MemoryStream();
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            if (factory is not null && Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory);
        }
    }
    private void Run()
    {
        OleInitialize(0);
        try
        {
            while (!work.IsCompleted)
            {
                if (work.TryTake(out var action, 20)) action();
                while (PeekMessageW(out var message, 0, 0, 0, 1))
                { TranslateMessage(ref message); DispatchMessageW(ref message); }
            }
        }
        finally { ReleaseShell(); OleUninitialize(); }
    }
    private void ReleaseShell()
    {
        foreach (object? obj in new[] { shellWindows, application })
        {
            try { if (obj is not null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
            catch (InvalidComObjectException) { }
        }
        shellWindows = null; application = null;
    }
    private QuickLookItem? Read(nint owner)
    {
        var objects = new List<object>();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        void CheckDeadline() { if (deadline.ElapsedMilliseconds >= 650) throw new TimeoutException(); }
        T Keep<T>(T value) { if (value is not null) objects.Add(value); return value; }
        try
        {
            dynamic shell = application ??= Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            CheckDeadline();
            dynamic windows = shellWindows ??= (object)shell.Windows();
            CheckDeadline();
            object? found = null;
            if (QuickLookNative.ClassName(owner) is "Progman" or "WorkerW")
            {
                object location = 0, root = null!; // reserved pvarLocRoot must be VT_EMPTY
                int desktopHwnd;
                found = Keep((object)windows.FindWindowSW(ref location, ref root, 8, out desktopHwnd, 1));
            }
            else
            {
                int count = windows.Count;
                for (int i = 0; i < count; i++)
                {
                    CheckDeadline();
                    dynamic browser = Keep((object)windows.Item(i));
                    CheckDeadline();
                    if ((nint)(long)browser.HWND == owner) { found = browser; break; }
                }
            }
            if (found is null) return null;
            CheckDeadline();
            dynamic document = Keep((object)((dynamic)found).Document);
            CheckDeadline();
            // FocusedItem can remain set with zero selected items after clicking the background.
            dynamic selected = Keep((object)document.SelectedItems());
            CheckDeadline();
            int selectedCount = selected.Count;
            CheckDeadline();
            if (selectedCount == 0) return QuickLookItem.Empty;
            dynamic? item = null;
            try { item = Keep((object?)document.FocusedItem); }
            catch (Exception) { /* old Shell views expose only SelectedItems */ }
            CheckDeadline();
            if (item is null) item = Keep((object)selected.Item(0));
            CheckDeadline();
            string name = item.Name ?? "Selected item";
            CheckDeadline();
            string path = item.Path ?? "";
            CheckDeadline();
            bool fileSystem = item.IsFileSystem;
            CheckDeadline();
            bool folder = item.IsFolder;
            CheckDeadline();
            string type = item.Type ?? "Item";
            return new(path.Length == 0 ? name : path, name,
                fileSystem && System.IO.Path.IsPathFullyQualified(path) ? path : null, folder, type);
        }
        catch (COMException)
        {
            ReleaseShell(); // Explorer may have restarted; reacquire the live root on the next read.
            throw;
        }
        finally
        {
            // Release in reverse order on their owning apartment, once per acquired reference.
            foreach (object obj in objects.AsEnumerable().Reverse())
            {
                try { if (Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
                catch (InvalidComObjectException) { }
            }
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) { queries.Dispose(); work.CompleteAdding(); }
        // Never join: Explorer may be unresponsive inside a COM call. Background thread exits when it returns.
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { void GetImage(Size size, uint flags, out nint bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
}
