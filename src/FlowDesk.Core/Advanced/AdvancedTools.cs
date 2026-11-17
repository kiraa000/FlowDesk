using System.Diagnostics;

namespace FlowDesk.Core.Advanced;

public sealed record AdvancedTool(string Id, string Name, string Publisher, string Description, string Website,
    string Executable, string[] RelativeFolders, string[] DisplayNames, string Arguments = "");

public static class AdvancedTools
{
    public static IReadOnlyList<AdvancedTool> All { get; } =
    [
        new("Microsoft.PowerToys", "Microsoft PowerToys", "Microsoft Corporation", "Utilities for arranging windows, finding files and everyday Windows tasks.",
            "https://learn.microsoft.com/windows/powertoys/", "PowerToys.exe", ["PowerToys", @"Microsoft\PowerToys", @"Programs\PowerToys"], ["PowerToys", "Microsoft PowerToys"], "--open-settings"),
        new("voidtools.Everything", "Everything", "voidtools", "Find files and folders by name almost instantly.",
            "https://www.voidtools.com/", "Everything.exe", ["Everything"], ["Everything"]),
        new("ShareX.ShareX", "ShareX", "ShareX Team", "Capture screenshots, record your screen and share files.",
            "https://getsharex.com/", "ShareX.exe", ["ShareX", @"Programs\ShareX"], ["ShareX"])
    ];
    public static AdvancedTool Get(string id) => All.Single(t => t.Id == id);
    public static IEnumerable<AdvancedTool> PaletteTools(bool enabled, IEnumerable<string> installedIds) =>
        enabled ? All.Where(t => installedIds.Contains(t.Id, StringComparer.Ordinal)) : [];
}

public sealed record ToolRegistration(string DisplayName, string? InstallLocation, string? DisplayIcon);
public interface IToolDetectionReader
{
    IEnumerable<ToolRegistration> UninstallEntries();
    IEnumerable<string> AppPaths(string executable);
    IEnumerable<string> InstallRoots();
    bool FileExists(string path);
}

public static class ToolDetection
{
    public static string? Find(AdvancedTool tool, IToolDetectionReader reader)
    {
        var paths = new List<string>();
        paths.AddRange(reader.AppPaths(tool.Executable));
        foreach (var entry in reader.UninstallEntries())
        {
            if (!tool.DisplayNames.Any(n => entry.DisplayName.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                entry.DisplayName.StartsWith(n + " (", StringComparison.OrdinalIgnoreCase) ||
                entry.DisplayName.StartsWith(n + " version ", StringComparison.OrdinalIgnoreCase) ||
                (entry.DisplayName.StartsWith(n + " ", StringComparison.OrdinalIgnoreCase) &&
                 Version.TryParse(entry.DisplayName[(n.Length + 1)..].Split(' ')[0], out _)))) continue;
            if (!string.IsNullOrWhiteSpace(entry.InstallLocation)) paths.Add(Path.Combine(entry.InstallLocation.Trim('"'), tool.Executable));
            if (!string.IsNullOrWhiteSpace(entry.DisplayIcon))
            {
                var icon = entry.DisplayIcon.Trim();
                if (icon.StartsWith('"')) icon = icon.Split('"').ElementAtOrDefault(1) ?? "";
                else if (icon.LastIndexOf(',') is var comma && comma >= 0) icon = icon[..comma];
                paths.Add(icon);
            }
        }
        foreach (var root in reader.InstallRoots())
            foreach (var folder in tool.RelativeFolders) paths.Add(Path.Combine(root, folder, tool.Executable));
        return paths.Select(p => p.Trim().Trim('"')).FirstOrDefault(p =>
            Path.IsPathFullyQualified(p) && Path.GetFileName(p).Equals(tool.Executable, StringComparison.OrdinalIgnoreCase) && reader.FileExists(p));
    }
}

public static class AdvancedConsent
{
    // A previous agreement timestamp is an audit record, never permission for the next off -> on transition.
    public static bool NeedsAgreement(bool currentlyEnabled, bool requestedEnabled) => !currentlyEnabled && requestedEnabled;
    public static async Task<AdvancedModeDecision> RequestAsync(bool enabled, DateTimeOffset? agreedAt, bool requested,
        Func<Task<bool>> confirm, Func<DateTimeOffset> now)
    {
        if (enabled == requested) return new(enabled, agreedAt);
        if (!requested) return new(false, agreedAt);
        return await confirm() ? new(true, now()) : new(false, agreedAt);
    }
}
public sealed record AdvancedModeDecision(bool Enabled, DateTimeOffset? AgreedAt);

public enum WingetOutcome { Success, AlreadyInstalled, Cancelled, Network, NotFound, Failed }
public static class ToolLaunch
{
    public const string StoreUri = "ms-windows-store://pdp/?productid=9NBLGGH4NNS1";
    public static ProcessStartInfo Winget(string executable, string id)
    {
        _ = AdvancedTools.Get(id); // Only curated packages; no arbitrary command input.
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "install", "--id", id, "-e", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" }) info.ArgumentList.Add(arg);
        return info;
    }
    public static ProcessStartInfo WinUtil(string windowsDirectory) => new(Path.Combine(windowsDirectory, @"System32\WindowsPowerShell\v1.0\powershell.exe"))
    {
        UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Normal,
        Arguments = "-NoProfile -NoExit -Command \"irm 'https://christitus.com/win' | iex\""
    };
    public static WingetOutcome Outcome(int exitCode) => unchecked((uint)exitCode) switch
    {
        0 => WingetOutcome.Success,
        0x8A15002B or 0x8A150061 or 0x8A15010D => WingetOutcome.AlreadyInstalled,
        0x800704C7 or 0x8A150005 or 0x8A15010C or 1223 or 1602 => WingetOutcome.Cancelled,
        0x80072EE7 or 0x80072EFD or 0x80072EE2 or 0x8A150008 or 0x8A150107 => WingetOutcome.Network,
        0x8A150014 => WingetOutcome.NotFound,
        _ => WingetOutcome.Failed
    };
    public static string ResultText(int code) => Outcome(code) switch
    {
        WingetOutcome.Success => "Install finished. Checking installation…",
        WingetOutcome.AlreadyInstalled => "Already installed. Checking installation…",
        WingetOutcome.Cancelled => "Install cancelled or administrator permission declined.",
        WingetOutcome.Network => "Install failed: check your internet connection and try again.",
        WingetOutcome.NotFound => "Install failed: package not found in the winget source.",
        _ => $"Install failed (code 0x{unchecked((uint)code):X8})."
    };
}
