using System.Runtime.InteropServices;

namespace FlowDesk.Core.Capture;

public interface IFolderPathSource
{
    /// <summary>Folder path shown by each open File Explorer window, keyed by window handle.</summary>
    IReadOnlyDictionary<long, string> GetOpenFolders();
}

/// <summary>
/// Reads open Explorer folders through the Shell.Application COM object. With Windows 11 tabs,
/// several entries share one window handle; the one matching the window title (active tab) wins.
/// </summary>
public sealed class ExplorerFolderPaths : IFolderPathSource
{
    public IReadOnlyDictionary<long, string> GetOpenFolders()
    {
        var result = new Dictionary<long, string>();
        var titles = new Dictionary<long, string>();
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return result;
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(type);
            dynamic windows = ((dynamic)shell!).Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic? w = windows.Item(i);
                    if (w is null) continue;
                    long hwnd = (long)w.HWND;
                    string? path = w.Document?.Folder?.Self?.Path;
                    string? name = w.LocationName;
                    if (string.IsNullOrEmpty(path)) continue; // Internet Explorer-hosted or non-folder views
                    if (!result.ContainsKey(hwnd) || (name is not null && TitleOf(hwnd).StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                        result[hwnd] = path;
                }
                catch (COMException) { }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            }
        }
        catch (COMException) { }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
        return result;

        string TitleOf(long h)
        {
            if (titles.TryGetValue(h, out var t)) return t;
            var sb = new System.Text.StringBuilder(512);
            Interop.Native.GetWindowTextW((nint)h, sb, sb.Capacity);
            return titles[h] = sb.ToString();
        }
    }
}
