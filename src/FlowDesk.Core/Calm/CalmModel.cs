using System.Text.Json;

namespace FlowDesk.Core.Calm;

public sealed record RegistryAddress(string Path, string Name, bool Machine = false);
// Data is a lossless JSON representation of the registry payload (binary uses base64).
public sealed record RegistryValue(int Kind, string Data)
{
    public static RegistryValue Dword(int value) => new(4, JsonSerializer.Serialize(value));
    public static RegistryValue Binary(byte[] value) => new(3, JsonSerializer.Serialize(value));
    public int? AsDword() => Kind == 4 && int.TryParse(Data, out int n) ? n : null;
    public byte[]? AsBinary() => Kind == 3 ? JsonSerializer.Deserialize<byte[]>(Data) : null;
}
public interface IRegistryAccess
{
    RegistryValue? Read(RegistryAddress address);
    void Write(RegistryAddress address, RegistryValue value);
    void Delete(RegistryAddress address);
    IReadOnlyList<string> ValueNames(string path);
    bool KeyExists(string path);
    void DeleteKeyIfEmpty(string path);
}
public sealed record StartupFile(string Name, string Command);
public interface IStartupFolder { IReadOnlyList<StartupFile> Entries(); }
public sealed record CalmValue(RegistryAddress Address, int Calm = 0, int On = 1, int? Default = 1);
public sealed record CalmSetting(string Id, string Group, string Title, string Description,
    IReadOnlyList<CalmValue> Values, string Effect, string Broadcast = "Policy",
    bool ExplorerRestart = false, bool SearchChoice = false, bool BuildGated = false);
public enum CalmState { Calm, Noisy, Custom, Managed, Unavailable }
public sealed record CalmReading(CalmSetting Setting, CalmState State, int? Choice, bool UsesDefault)
{
    public bool IsFinding => State == CalmState.Noisy;
}
public sealed record WindowsBuild(int Build, int Revision)
{
    public bool HasExplorerRecommendations => Build > 26200 ||
        (Build is 26100 or 26200 && Revision >= 7309);
}
public static class CalmEvaluator
{
    public static CalmReading Evaluate(CalmSetting setting, Func<RegistryAddress, RegistryValue?> read, bool managed = false)
    {
        if (managed) return new(setting, CalmState.Managed, null, false);
        bool missing = false;
        var values = setting.Values.Select(v =>
        {
            var raw = read(v.Address);
            missing |= raw is null;
            return raw is null ? v.Default : raw.AsDword();
        }).ToArray();
        if (values.Any(v => v is null)) return new(setting, CalmState.Custom, null, missing);
        if (setting.SearchChoice)
        {
            int n = values[0]!.Value;
            return new(setting, n == 0 ? CalmState.Calm : n is 1 or 2 ? CalmState.Noisy : CalmState.Custom, n is >= 0 and <= 2 ? n : null, missing);
        }
        // A mixed pair is still noisy if any known component is on; unexpected data is never a finding.
        if (values.Where((n, i) => n != setting.Values[i].Calm && n != setting.Values[i].On).Any())
            return new(setting, CalmState.Custom, null, missing);
        return new(setting, values.Where((n, i) => n != setting.Values[i].Calm).Any() ? CalmState.Noisy : CalmState.Calm, null, missing);
    }
    public static IReadOnlyList<CalmReading> Scan(IRegistryAccess registry, WindowsBuild build) =>
        CalmCatalog.All.Where(s => !s.BuildGated || build.HasExplorerRecommendations).Select(s =>
        {
            try { return Evaluate(s, registry.Read, IsManaged(s, registry)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { return new CalmReading(s, CalmState.Unavailable, null, false); }
        }).ToArray();

    public static bool IsManaged(CalmSetting s, IRegistryAccess registry)
    {
        return CalmPolicies.IsEnforced(s, registry);
    }
}
public static class StartupApprovedCodec
{
    public static bool? Enabled(RegistryValue? value)
    {
        if (value is null) return true;
        byte[]? bytes;
        try { bytes = value.AsBinary(); }
        catch (Exception ex) when (ex is JsonException or FormatException) { return null; }
        return bytes is { Length: 12 } && bytes[0] is 2 or 3 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0
            ? bytes[0] == 2 : null;
    }
    public static RegistryValue Encode(bool enabled, RegistryValue? previous)
    {
        if (previous is not null && Enabled(previous) is null) throw new InvalidOperationException("Custom startup approval cannot be changed.");
        byte[] bytes = previous?.AsBinary() ?? new byte[12];
        bytes[0] = enabled ? (byte)2 : (byte)3; // Preserve the timestamp, including zero, byte-for-byte.
        return RegistryValue.Binary(bytes);
    }
}
public sealed record StartupEntry(string Id, string Name, string Command, RegistryAddress Approval, bool? Enabled);
public static class StartupCatalog
{
    public const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    public static IReadOnlyList<StartupEntry> Scan(IRegistryAccess registry, IStartupFolder folder)
    {
        var result = new List<StartupEntry>();
        foreach (string name in registry.ValueNames(Run))
        {
            var command = registry.Read(new(Run, name));
            if (command is null || command.Kind is not (1 or 2)) continue;
            var approval = new RegistryAddress(Approved + "Run", name);
            result.Add(new("startup-run:" + name, name, JsonSerializer.Deserialize<string>(command.Data) ?? "", approval, StartupApprovedCodec.Enabled(registry.Read(approval))));
        }
        foreach (var file in folder.Entries())
        {
            var approval = new RegistryAddress(Approved + "StartupFolder", file.Name);
            result.Add(new("startup-folder:" + file.Name, file.Name, file.Command, approval, StartupApprovedCodec.Enabled(registry.Read(approval))));
        }
        return result;
    }
}
