using System.Diagnostics;
using System.Text;
using FlowDesk.Core.Model;
using static FlowDesk.Core.Interop.Native;

namespace FlowDesk.Core.Restore;

public sealed record LaunchResult(bool Ok, string? Error = null, string? HostExe = null)
{
    public static LaunchResult Fail(string error) => new(false, error);
}

public interface IAppLauncher
{
    /// <summary>Starts the item. <see cref="LaunchResult.HostExe"/> is the exe expected to show the window when known.</summary>
    LaunchResult Launch(WorkspaceItem item);
}

/// <summary>Launches items through the shell, the way the user would.</summary>
public sealed class ShellLauncher : IAppLauncher
{
    private static readonly string[] Chromium = ["chrome", "msedge", "brave", "vivaldi", "opera", "opera_gx", "chromium", "arc"];
    private static readonly string[] Gecko = ["firefox", "librewolf", "waterfox", "floorp", "zen"];

    public static bool IsBrowser(string? exePath)
    {
        string n = Model.AppKeys.ExeName(exePath);
        return Chromium.Contains(n) || Gecko.Contains(n);
    }

    public LaunchResult Launch(WorkspaceItem item)
    {
        try
        {
            return item.Kind switch
            {
                ItemKind.App => LaunchApp(item.App, BrowserSession.Applies(item)),
                ItemKind.Website => LaunchUrls(item.Urls, item.App?.ExePath),
                ItemKind.File => LaunchFile(item),
                ItemKind.Folder => LaunchFolder(item.Path),
                _ => LaunchResult.Fail("Unknown item type"),
            };
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return LaunchResult.Fail(ex.NativeErrorCode == 1223 ? "Launch was cancelled" : ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return LaunchResult.Fail(ex.Message);
        }
    }

    /// <param name="browserSession">Browser "reopen my last tabs": pass only the profile arguments, never links.</param>
    private static LaunchResult LaunchApp(AppRef? app, bool browserSession = false)
    {
        if (app is null) return LaunchResult.Fail("No program set");
        if (!string.IsNullOrEmpty(app.Aumid))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.Aumid}") { UseShellExecute = true });
            return new LaunchResult(true, HostExe: app.ExePath);
        }
        string? exe = File.Exists(app.ExePath) ? app.ExePath : FindUpdatedExe(app.ExePath);
        if (exe is null) return LaunchResult.Fail($"Program not found: {app.ExePath}");
        string args = (browserSession ? BrowserSession.ProfileArgs(app.ExePath, app.Arguments) : app.Arguments) ?? "";
        Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = true,
            WorkingDirectory = exe == app.ExePath && app.WorkingDirectory is not null ? app.WorkingDirectory : Path.GetDirectoryName(exe) ?? "",
        });
        return new LaunchResult(true, HostExe: exe);
    }

    /// <summary>
    /// Self-updating apps (Discord, Slack, Teams classic - "Squirrel" installs) keep the exe in a versioned
    /// folder like ...\Discord\app-1.0.9267\Discord.exe that disappears on update. Find the newest sibling.
    /// </summary>
    public static string? FindUpdatedExe(string exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            var root = dir is null ? null : Path.GetDirectoryName(dir);
            if (dir is null || root is null || !Path.GetFileName(dir).StartsWith("app-", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root))
                return null;
            string file = Path.GetFileName(exePath);
            return Directory.GetDirectories(root, "app-*")
                .Select(d => (Dir: d, Ver: Version.TryParse(Path.GetFileName(d)[4..], out var v) ? v : null))
                .Where(x => x.Ver is not null && File.Exists(Path.Combine(x.Dir, file)))
                .OrderByDescending(x => x.Ver)
                .Select(x => Path.Combine(x.Dir, file))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Opens all URLs in ONE new window of the chosen (or default) browser so the window can be placed.</summary>
    private static LaunchResult LaunchUrls(List<string> urls, string? preferredBrowser)
    {
        var valid = urls.Where(IsWebUrl).ToList();
        if (valid.Count == 0) return LaunchResult.Fail("No valid http(s) links");
        string? browser = !string.IsNullOrEmpty(preferredBrowser) && File.Exists(preferredBrowser) ? preferredBrowser : DefaultBrowserExe();
        string name = Model.AppKeys.ExeName(browser);
        string quoted = string.Join(' ', valid.Select(u => "\"" + u.Replace("\"", "%22") + "\""));

        if (browser is not null && Chromium.Contains(name))
            Process.Start(new ProcessStartInfo(browser, "--new-window " + quoted) { UseShellExecute = false });
        else if (browser is not null && Gecko.Contains(name))
            Process.Start(new ProcessStartInfo(browser, "-new-window " + quoted) { UseShellExecute = false });
        else
        {
            foreach (var u in valid) Process.Start(new ProcessStartInfo(u) { UseShellExecute = true });
            return new LaunchResult(true, HostExe: browser); // tabs may land in an existing window
        }
        return new LaunchResult(true, HostExe: browser);
    }

    public static bool IsWebUrl(string u) =>
        Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static LaunchResult LaunchFile(WorkspaceItem item)
    {
        if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path)) return LaunchResult.Fail($"File not found: {item.Path}");
        if (item.App is { ExePath.Length: > 0 } app && File.Exists(app.ExePath))
        {
            Process.Start(new ProcessStartInfo(app.ExePath, "\"" + item.Path + "\"") { UseShellExecute = true });
            return new LaunchResult(true, HostExe: app.ExePath);
        }
        Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        return new LaunchResult(true, HostExe: AssociatedExe(Path.GetExtension(item.Path)));
    }

    private static LaunchResult LaunchFolder(string? path)
    {
        if (string.IsNullOrEmpty(path)) return LaunchResult.Fail("No folder set");
        bool shellPath = path.StartsWith("::", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
        if (!shellPath && !Directory.Exists(path)) return LaunchResult.Fail($"Folder not found: {path}");
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        return new LaunchResult(true, HostExe: "explorer.exe");
    }

    public static string? DefaultBrowserExe() => AssociatedExe("http");

    /// <summary>Exe registered for an extension (".pdf") or protocol ("http").</summary>
    public static string? AssociatedExe(string assoc)
    {
        int len = 1024;
        var sb = new StringBuilder(len);
        int hr = AssocQueryStringW(0, ASSOCSTR_EXECUTABLE, assoc, "open", sb, ref len);
        string s = hr == 0 ? sb.ToString() : "";
        return s.Length > 0 && File.Exists(s) ? s : null;
    }
}
