using System.Reflection;

namespace FlowDesk.App.Services;

/// <summary>
/// The version shown in Settings, read from the built assembly metadata. The csproj is the only
/// source: InformationalVersion is set from Version there and carries no git revision suffix.
/// </summary>
public static class AppVersion
{
    public static string Display { get; } = Read();

    private static string Read()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int metadata = informational.IndexOf('+');
            return metadata < 0 ? informational : informational[..metadata];
        }
        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
