using System.Globalization;
using System.Text.Json;

namespace FlowDesk.Core.Updates;

/// <summary>
/// A strict semantic version (major.minor.patch with an optional dot-separated prerelease).
/// Build metadata is accepted on input and dropped; <see cref="ToString"/> is canonical.
/// </summary>
public sealed record UpdateVersion(int Major, int Minor, int Patch, string? Prerelease)
    : IComparable<UpdateVersion>
{
    // Keeps parsing bounded; real tags are a few dozen characters at most.
    private const int MaxTextLength = 128;

    public static bool TryParse(string? text, out UpdateVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength) return false;

        int index = 0;
        if (text[index] == 'v') index++;
        if (!TryReadNumber(text, ref index, out int major)) return false;
        if (!TryConsume(text, ref index, '.')) return false;
        if (!TryReadNumber(text, ref index, out int minor)) return false;
        if (!TryConsume(text, ref index, '.')) return false;
        if (!TryReadNumber(text, ref index, out int patch)) return false;

        string? prerelease = null;
        if (index < text.Length && text[index] == '-')
        {
            int start = ++index;
            while (index < text.Length && text[index] != '+') index++;
            prerelease = text[start..index];
            if (!IsValidPrerelease(prerelease)) return false;
        }

        if (index < text.Length && text[index] == '+')
        {
            int start = ++index;
            while (index < text.Length && text[index] != '+') index++;
            if (!IsValidBuildMetadata(text[start..index])) return false;
        }

        if (index != text.Length) return false;
        version = new UpdateVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(UpdateVersion? other)
    {
        if (other is null) return 1;
        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    private static bool TryReadNumber(string text, ref int index, out int value)
    {
        value = 0;
        int start = index;
        while (index < text.Length && IsAsciiDigit(text[index])) index++;
        int length = index - start;
        if (length == 0) return false;
        // No leading zero unless the component is exactly "0".
        if (length > 1 && text[start] == '0') return false;
        return int.TryParse(text.AsSpan(start, length), NumberStyles.None,
            CultureInfo.InvariantCulture, out value);
    }

    private static bool TryConsume(string text, ref int index, char expected)
    {
        if (index >= text.Length || text[index] != expected) return false;
        index++;
        return true;
    }

    private static bool IsValidPrerelease(string text)
    {
        if (text.Length == 0) return false;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '.') continue;
            int length = i - start;
            if (length == 0) return false;
            bool numeric = true;
            for (int j = start; j < i; j++)
            {
                char c = text[j];
                if (!IsAsciiDigit(c) && !IsAsciiLetter(c) && c != '-') return false;
                if (!IsAsciiDigit(c)) numeric = false;
            }
            // Numeric prerelease identifiers must not have leading zeros.
            if (numeric && length > 1 && text[start] == '0') return false;
            start = i + 1;
        }
        return true;
    }

    private static bool IsValidBuildMetadata(string text)
    {
        if (text.Length == 0) return false;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '.') continue;
            if (i == start) return false;
            for (int j = start; j < i; j++)
            {
                char c = text[j];
                if (!IsAsciiDigit(c) && !IsAsciiLetter(c) && c != '-') return false;
            }
            start = i + 1;
        }
        return true;
    }

    private static int ComparePrerelease(string left, string right)
    {
        string[] a = left.Split('.');
        string[] b = right.Split('.');
        int shared = Math.Min(a.Length, b.Length);
        for (int i = 0; i < shared; i++)
        {
            bool aNumeric = IsNumericIdentifier(a[i]);
            bool bNumeric = IsNumericIdentifier(b[i]);
            int result = aNumeric && bNumeric ? CompareNumericIdentifier(a[i], b[i])
                : aNumeric ? -1
                : bNumeric ? 1
                : string.CompareOrdinal(a[i], b[i]);
            if (result != 0) return result;
        }
        return a.Length.CompareTo(b.Length);
    }

    private static int CompareNumericIdentifier(string a, string b)
    {
        // Parsed identifiers have no leading zeros, so a longer number is larger.
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        return string.CompareOrdinal(a, b);
    }

    private static bool IsNumericIdentifier(string identifier)
    {
        foreach (char c in identifier)
            if (!IsAsciiDigit(c)) return false;
        return identifier.Length > 0;
    }

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}

/// <summary>
/// A parsed GitHub latest-release document. Only the fields the updater needs are kept;
/// asset origins are deliberately not validated here (the transport layer owns that).
/// </summary>
public sealed record UpdateRelease(UpdateVersion Version, string Tag, Uri? ReleaseNotes,
    Uri? Installer, Uri? Checksums)
{
    private const string ReleasesPath = "/kiraa000/FlowDesk/releases";
    private const string ChecksumsAssetName = "SHA256SUMS.txt";

    /// <summary>Parses a GitHub release JSON document; returns null for unusable releases.</summary>
    public static UpdateRelease? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;

            if (!root.TryGetProperty("tag_name", out JsonElement tagElement) ||
                tagElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string tag = tagElement.GetString()!;
            if (!UpdateVersion.TryParse(tag, out UpdateVersion? version) || version is null) return null;

            Uri? notes = null;
            if (root.TryGetProperty("html_url", out JsonElement notesElement) &&
                notesElement.ValueKind == JsonValueKind.String)
            {
                notes = SafeReleaseNotes(notesElement.GetString());
            }

            Uri? installer = null;
            Uri? checksums = null;
            if (root.TryGetProperty("assets", out JsonElement assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                string installerName = $"FlowDesk-{version}-win-x64-setup.exe";
                Uri? installerCandidate = null;
                Uri? checksumsCandidate = null;
                int installerCount = 0;
                int checksumsCount = 0;

                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    if (asset.ValueKind != JsonValueKind.Object) continue;
                    if (!asset.TryGetProperty("name", out JsonElement nameElement) ||
                        nameElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string name = nameElement.GetString()!;
                    bool isInstaller = string.Equals(name, installerName, StringComparison.Ordinal);
                    bool isChecksums = string.Equals(name, ChecksumsAssetName, StringComparison.Ordinal);
                    if (!isInstaller && !isChecksums) continue;

                    Uri? url = null;
                    if (asset.TryGetProperty("browser_download_url", out JsonElement urlElement) &&
                        urlElement.ValueKind == JsonValueKind.String)
                    {
                        Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out url);
                    }

                    if (isInstaller)
                    {
                        installerCount++;
                        installerCandidate = url;
                    }
                    else
                    {
                        checksumsCount++;
                        checksumsCandidate = url;
                    }
                }

                if (installerCount == 1) installer = installerCandidate;
                if (checksumsCount == 1) checksums = checksumsCandidate;
            }

            return new UpdateRelease(version, tag, notes, installer, checksums);
        }
    }

    /// <summary>True only when the release is strictly newer than the running version and not skipped.</summary>
    public static bool ShouldOffer(UpdateRelease release, UpdateVersion current, string? skipped)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(current);
        if (release.Version.CompareTo(current) <= 0) return false;
        if (!string.IsNullOrWhiteSpace(skipped) &&
            UpdateVersion.TryParse(skipped, out UpdateVersion? skippedVersion) &&
            skippedVersion is not null &&
            skippedVersion.Equals(release.Version))
        {
            return false;
        }
        return true;
    }

    private static bool IsTrue(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement element) &&
        element.ValueKind == JsonValueKind.True;

    private static Uri? SafeReleaseNotes(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return null;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (!uri.IsDefaultPort || uri.Port != 443) return null;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return null;

        string path = uri.AbsolutePath;
        if (!string.Equals(path, ReleasesPath, StringComparison.Ordinal) &&
            !path.StartsWith(ReleasesPath + "/", StringComparison.Ordinal))
        {
            return null;
        }
        return uri;
    }
}

/// <summary>Reads a sha256sum-style manifest without touching the filesystem.</summary>
public static class UpdateChecksums
{
    public static string? Find(string text, string fileName)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fileName)) return null;

        string? match = null;
        int matches = 0;
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            if (TryReadEntry(line, fileName, out string hash))
            {
                matches++;
                if (matches > 1) return null;
                match = hash;
                continue;
            }
            // A line that names this file but does not parse is a malformed matching entry.
            if (TargetsFile(line, fileName)) return null;
        }
        return matches == 1 ? match : null;
    }

    private static bool TryReadEntry(string line, string fileName, out string hash)
    {
        hash = "";
        if (!line.EndsWith(fileName, StringComparison.Ordinal)) return false;
        string prefix = line[..^fileName.Length];
        if (prefix.EndsWith('*')) prefix = prefix[..^1];

        int end = prefix.Length;
        while (end > 0 && prefix[end - 1] == ' ') end--;
        // sha256sum separates the digest and the file name with at least one space.
        if (end == prefix.Length || end != 64) return false;

        for (int i = 0; i < 64; i++)
            if (!IsHex(prefix[i])) return false;

        hash = prefix[..64].ToUpperInvariant();
        return true;
    }

    private static bool TargetsFile(string line, string fileName)
    {
        if (!line.EndsWith(fileName, StringComparison.Ordinal)) return false;
        if (line.Length == fileName.Length) return true;
        char before = line[line.Length - fileName.Length - 1];
        return before is ' ' or '\t' or '*';
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
