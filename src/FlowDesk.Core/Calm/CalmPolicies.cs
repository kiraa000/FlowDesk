using System.Text.Json;

namespace FlowDesk.Core.Calm;

/// <summary>Registry group-policy values, not CSP Allow* values (whose polarity is inverted).
/// Edition applicability: Microsoft Learn policy-csp-experience, AllowWindowsConsumerFeatures /
/// AllowWindowsSpotlight. A leftover policy value is not proof that this Windows edition enforces it.</summary>
public static class CalmPolicies
{
    private const string Cloud = @"SOFTWARE\Policies\Microsoft\Windows\CloudContent";
    private const string EditionPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private sealed record Policy(string Path, string Name, bool Machine, int EnforcingValue, bool EnterpriseOnly = false);
    public static bool IsEnforced(CalmSetting row, IRegistryAccess registry)
    {
        var edition = registry.Read(new(EditionPath, "EditionID", true));
        bool enterprise = IsEnterpriseEdition(edition);
        if (!enterprise && !IsProfessionalEdition(edition)) return false;
        var policies = new List<Policy>();
        if (row.Id == "taskbar-widgets") policies.Add(new(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", true, 0));
        if (row.SearchChoice)
        {
            // This policy explicitly locks each of the four supported search appearances, including hidden.
            for (int i = 0; i <= 3; i++) policies.Add(new(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "SearchOnTaskbarMode", true, i));
        }
        if (row.Values.Any(v => v.Address.Path.Contains("ContentDeliveryManager", StringComparison.OrdinalIgnoreCase)))
            policies.Add(new(Cloud, "DisableWindowsSpotlightFeatures", false, 1, true));
        // Consumer experiences is not a master lock for tips, Settings suggestions or lock-screen facts.
        if (row.Id == "start-suggestions") policies.Add(new(Cloud, "DisableWindowsConsumerFeatures", true, 1, true));
        if (row.Id == "windows-tips") policies.Add(new(Cloud, "DisableSoftLanding", true, 1, true));
        if (row.Id == "welcome-experience") policies.Add(new(Cloud, "DisableWindowsSpotlightWindowsWelcomeExperience", false, 1, true));
        if (row.Id == "settings-suggested-content") policies.Add(new(Cloud, "DisableWindowsSpotlightOnSettings", false, 1, true));
        // HideRecommendedPersonalizedSites affects websites, not Start_IrisRecommendations: no false lock.
        return policies.Any(p => (!p.EnterpriseOnly || enterprise) && registry.Read(new(p.Path, p.Name, p.Machine))?.AsDword() == p.EnforcingValue);
    }
    public static bool IsEnterpriseEdition(RegistryValue? raw)
    {
        string? edition = EditionName(raw);
        return edition is not null && new[] { "Enterprise", "EnterpriseN", "EnterpriseS", "EnterpriseSN", "EnterpriseG", "EnterpriseGN", "Education", "EducationN", "IoTEnterprise", "IoTEnterpriseS", "IoTEnterpriseSK" }
            .Contains(edition, StringComparer.OrdinalIgnoreCase);
    }
    private static bool IsProfessionalEdition(RegistryValue? raw) => EditionName(raw) is { } edition &&
        new[] { "Professional", "ProfessionalN", "ProfessionalEducation", "ProfessionalEducationN", "ProfessionalWorkstation", "ProfessionalWorkstationN" }.Contains(edition, StringComparer.OrdinalIgnoreCase);
    private static string? EditionName(RegistryValue? raw)
    {
        if (raw is not { Kind: 1 }) return null;
        try { return JsonSerializer.Deserialize<string>(raw.Data); }
        catch (JsonException) { return null; }
    }
}
