using FlowDesk.Core.Updates;

namespace FlowDesk.Core.Tests;

public sealed class UpdateReleaseTests
{
    private const string SetupName = "FlowDesk-1.2.1-win-x64-setup.exe";

    // ---- Version parsing -------------------------------------------------

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("0.0.0", "0.0.0")]
    [InlineData("2147483647.0.1", "2147483647.0.1")]
    [InlineData("1.2.3+build.5", "1.2.3")]
    [InlineData("v1.2.3-beta.2+sha.abc", "1.2.3-beta.2")]
    [InlineData("1.2.3-rc.1.0", "1.2.3-rc.1.0")]
    public void ParsesVersionsAndDropsMetadata(string text, string canonical)
    {
        Assert.True(UpdateVersion.TryParse(text, out UpdateVersion? version));
        Assert.NotNull(version);
        Assert.Equal(canonical, version!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.-3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3-alpha..1")]
    [InlineData("1.2.3-alpha_1")]
    [InlineData("1.2.3 alpha")]
    [InlineData("V1.2.3")]
    [InlineData("1.2.3-alpha.01")]
    [InlineData("1.2.3+meta+extra")]
    [InlineData("2147483648.0.0")]
    [InlineData("1.2147483648.0")]
    [InlineData("1.2.2147483648")]
    public void RejectsInvalidVersions(string? text)
    {
        Assert.False(UpdateVersion.TryParse(text, out UpdateVersion? version));
        Assert.Null(version);
    }

    [Fact]
    public void RejectsOverlongText()
    {
        string text = "1.2.3-" + new string('a', 200);
        Assert.False(UpdateVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.2.4", "1.2.3", 1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("v1.2.3", "1.2.3+meta", 0)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-alpha", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta", "1.0.0-beta.2", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("1.0.0-alpha.10", "1.0.0-alpha.9", 1)]
    [InlineData("1.0.0-alpha.99999999999999999999", "1.0.0-alpha.9", 1)]
    [InlineData("1.0.0-2", "1.0.0-alpha", -1)]
    public void ComparesSemverOrder(string left, string right, int expected)
    {
        Assert.True(UpdateVersion.TryParse(left, out UpdateVersion? a));
        Assert.True(UpdateVersion.TryParse(right, out UpdateVersion? b));
        Assert.Equal(expected, Math.Sign(a!.CompareTo(b)));
        Assert.Equal(-expected, Math.Sign(b!.CompareTo(a)));
        Assert.Equal(expected == 0, a.Equals(b));
    }

    [Fact]
    public void CompareToNullIsGreater()
    {
        Assert.True(UpdateVersion.TryParse("1.2.3", out UpdateVersion? version));
        Assert.Equal(1, version!.CompareTo(null));
    }

    // ---- Release JSON ----------------------------------------------------

    [Fact]
    public void ParsesAReleaseWithBothAssets()
    {
        string json = ReleaseJson(assets: AssetsFor("1.2.1", "v1.2.1"));

        UpdateRelease? release = UpdateRelease.Parse(json);

        Assert.NotNull(release);
        Assert.Equal("1.2.1", release!.Version.ToString());
        Assert.Equal("v1.2.1", release.Tag);
        Assert.Equal("https://github.com/kiraa000/FlowDesk/releases/tag/v1.2.1",
            release.ReleaseNotes?.AbsoluteUri);
        Assert.Equal("https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/" + SetupName,
            release.Installer?.AbsoluteUri);
        Assert.Equal("https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt",
            release.Checksums?.AbsoluteUri);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IgnoresDraftAndPrereleaseReleases(bool draft, bool prerelease)
    {
        Assert.Null(UpdateRelease.Parse(ReleaseJson(draft: draft, prerelease: prerelease)));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v1.2")]
    [InlineData("")]
    [InlineData("1.2.3.4")]
    public void InvalidTagGivesNoRelease(string tag)
    {
        Assert.Null(UpdateRelease.Parse(ReleaseJson(tag: tag)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    public void UnusableJsonGivesNoRelease(string json)
    {
        Assert.Null(UpdateRelease.Parse(json));
    }

    [Fact]
    public void TagWithMetadataIsKeptButVersionIsCanonical()
    {
        UpdateRelease? release = UpdateRelease.Parse(
            ReleaseJson(tag: "v1.2.1+push.7", assets: AssetsFor("1.2.1", "v1.2.1+push.7")));

        Assert.NotNull(release);
        Assert.Equal("v1.2.1+push.7", release!.Tag);
        Assert.Equal("1.2.1", release.Version.ToString());
        Assert.NotNull(release.Installer);
    }

    [Fact]
    public void TagWithoutVPrefixStillMatchesCanonicalAssetName()
    {
        UpdateRelease? release = UpdateRelease.Parse(
            ReleaseJson(tag: "1.2.1", assets: AssetsFor("1.2.1", "1.2.1")));

        Assert.NotNull(release);
        Assert.NotNull(release!.Installer);
    }

    // ---- Release notes URL safety ---------------------------------------

    [Theory]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases")]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/")]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/tag/v1.2.1")]
    [InlineData("https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt")]
    public void AcceptsSafeReleaseNotesUrls(string url)
    {
        UpdateRelease? release = UpdateRelease.Parse(ReleaseJson(htmlUrl: url));
        Assert.NotNull(release);
        Assert.NotNull(release!.ReleaseNotes);
    }

    [Theory]
    [InlineData("http://github.com/kiraa000/FlowDesk/releases/tag/v1.2.1")]
    [InlineData("https://evil.example/kiraa000/FlowDesk/releases/tag/v1.2.1")]
    [InlineData("https://github.com.evil.example/kiraa000/FlowDesk/releases/tag/v1.2.1")]
    [InlineData("https://github.com/kiraa000/FlowDesk/issues")]
    [InlineData("https://github.com/kiraa000/FlowDesk/releasesevil")]
    [InlineData("https://github.com/kiraa000/flowdesk/releases/tag/v1.2.1")]
    [InlineData("https://user@github.com/kiraa000/FlowDesk/releases/tag/v1.2.1")]
    [InlineData("https://user:pass@github.com/kiraa000/FlowDesk/releases/tag/v1.2.1")]
    [InlineData("https://github.com:8443/kiraa000/FlowDesk/releases/tag/v1.2.1")]
    [InlineData("not a url")]
    [InlineData("")]
    public void RejectsUnsafeReleaseNotesUrls(string url)
    {
        UpdateRelease? release = UpdateRelease.Parse(ReleaseJson(htmlUrl: url));
        Assert.NotNull(release);
        Assert.Null(release!.ReleaseNotes);
    }

    // ---- Asset selection -------------------------------------------------

    [Fact]
    public void WrongVersionAssetLeavesInstallerNull()
    {
        UpdateRelease? release = UpdateRelease.Parse(ReleaseJson(assets: new (string Name, string? Url)[]
        {
            ("FlowDesk-1.2.2-win-x64-setup.exe", "https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/x.exe"),
            ("SHA256SUMS.txt", "https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt"),
        }));

        Assert.NotNull(release);
        Assert.Null(release!.Installer);
        Assert.NotNull(release.Checksums);
    }

    [Fact]
    public void MissingAssetsStayNull()
    {
        UpdateRelease? release = UpdateRelease.Parse(ReleaseJson());

        Assert.NotNull(release);
        Assert.Null(release!.Installer);
        Assert.Null(release.Checksums);
    }

    [Fact]
    public void DuplicateInstallerAssetLeavesInstallerNull()
    {
        string url = "https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/" + SetupName;
        UpdateRelease? release = UpdateRelease.Parse(ReleaseJson(assets: new (string Name, string? Url)[]
        {
            (SetupName, url),
            (SetupName, url),
            ("SHA256SUMS.txt", "https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt"),
        }));

        Assert.NotNull(release);
        Assert.Null(release!.Installer);
        Assert.NotNull(release.Checksums);
    }

    [Fact]
    public void DuplicateChecksumsAssetLeavesChecksumsNull()
    {
        string url = "https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt";
        UpdateRelease? release = UpdateRelease.Parse(ReleaseJson(assets: new (string Name, string? Url)[]
        {
            (SetupName, "https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/" + SetupName),
            ("SHA256SUMS.txt", url),
            ("SHA256SUMS.txt", url),
        }));

        Assert.NotNull(release);
        Assert.NotNull(release!.Installer);
        Assert.Null(release.Checksums);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/releases/download/v1.2.1/" + SetupName)]
    [InlineData("")]
    public void InvalidAssetUrlLeavesInstallerNull(string url)
    {
        UpdateRelease? release = UpdateRelease.Parse(ReleaseJson(assets: new (string Name, string? Url)[]
        {
            (SetupName, url),
            ("SHA256SUMS.txt", "https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt"),
        }));

        Assert.NotNull(release);
        Assert.Null(release!.Installer);
        Assert.NotNull(release.Checksums);
    }

    [Fact]
    public void AssetWithMissingUrlIsIgnored()
    {
        UpdateRelease? release = UpdateRelease.Parse(ReleaseJson(assets: new (string Name, string? Url)[]
        {
            (SetupName, null),
            ("SHA256SUMS.txt", "https://github.com/kiraa000/FlowDesk/releases/download/v1.2.1/SHA256SUMS.txt"),
        }));

        Assert.NotNull(release);
        Assert.Null(release!.Installer);
        Assert.NotNull(release.Checksums);
    }

    // ---- ShouldOffer -----------------------------------------------------

    [Theory]
    [InlineData("v1.2.2", "1.2.1", null, true)]
    [InlineData("v1.2.1", "1.2.1", null, false)]
    [InlineData("v1.2.0", "1.2.1", null, false)]
    [InlineData("v1.2.1", "1.2.1-beta", null, true)]
    [InlineData("v1.2.1-beta", "1.2.1", null, false)]
    [InlineData("v1.2.2", "1.2.1", "v1.2.2", false)]
    [InlineData("v1.2.2", "1.2.1", "1.2.2", false)]
    [InlineData("v1.2.2", "1.2.1", "1.2.2+meta", false)]
    [InlineData("v1.2.2", "1.2.1", "v1.2.1", true)]
    [InlineData("v1.2.2", "1.2.1", "not-a-version", true)]
    [InlineData("v1.2.2", "1.2.1", "", true)]
    public void ShouldOfferHonoursNewerAndSkipped(string tag, string current, string? skipped, bool expected)
    {
        UpdateRelease release = UpdateRelease.Parse(ReleaseJson(tag: tag))!;
        Assert.True(UpdateVersion.TryParse(current, out UpdateVersion? currentVersion));
        Assert.Equal(expected, UpdateRelease.ShouldOffer(release, currentVersion!, skipped));
    }

    // ---- SHA256SUMS ------------------------------------------------------

    [Fact]
    public void FindsChecksumAndUppercasesIt()
    {
        string hash = new('a', 64);
        Assert.Equal(new string('A', 64), UpdateChecksums.Find($"{hash}  {SetupName}\n", SetupName));
    }

    [Fact]
    public void AcceptsBinaryMarker()
    {
        string hash = new('b', 64);
        Assert.Equal(new string('B', 64), UpdateChecksums.Find($"{hash} *{SetupName}", SetupName));
    }

    [Fact]
    public void AcceptsCrlf()
    {
        string hash = new('c', 64);
        string text = $"{new string('d', 64)}  other.zip\r\n{hash}  {SetupName}\r\n";
        Assert.Equal(new string('C', 64), UpdateChecksums.Find(text, SetupName));
    }

    [Fact]
    public void IgnoresEntriesForOtherFiles()
    {
        string hash = new('e', 64);
        string text = $"{new string('f', 64)}  SHA256SUMS.txt\n{hash}  {SetupName}\n";
        Assert.Equal(new string('E', 64), UpdateChecksums.Find(text, SetupName));
    }

    [Fact]
    public void MissingEntryReturnsNull()
    {
        Assert.Null(UpdateChecksums.Find("", SetupName));
        Assert.Null(UpdateChecksums.Find($"{new string('a', 64)}  other.exe\n", SetupName));
    }

    [Fact]
    public void DuplicateEntryReturnsNull()
    {
        string line = $"{new string('a', 64)}  {SetupName}\n";
        Assert.Null(UpdateChecksums.Find(line + line, SetupName));
    }

    [Fact]
    public void DuplicateIdenticalBinaryAndTextEntriesReturnNull()
    {
        string text = $"{new string('a', 64)}  {SetupName}\n{new string('a', 64)} *{SetupName}\n";
        Assert.Null(UpdateChecksums.Find(text, SetupName));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void MalformedMatchingEntryReturnsNull(string hash)
    {
        Assert.Null(UpdateChecksums.Find($"{hash}  {SetupName}\n", SetupName));
    }

    [Fact]
    public void NonHexMatchingEntryReturnsNull()
    {
        Assert.Null(UpdateChecksums.Find($"{new string('z', 64)}  {SetupName}\n", SetupName));
    }

    [Fact]
    public void MissingSeparatorReturnsNull()
    {
        Assert.Null(UpdateChecksums.Find($"{new string('a', 64)}{SetupName}\n", SetupName));
    }

    [Fact]
    public void VeryLongHashIsMalformed()
    {
        Assert.Null(UpdateChecksums.Find($"{new string('a', 65)}  {SetupName}\n", SetupName));
    }

    // ---- Helpers ---------------------------------------------------------

    private static string ReleaseJson(
        string tag = "v1.2.1",
        bool draft = false,
        bool prerelease = false,
        string? htmlUrl = "https://github.com/kiraa000/FlowDesk/releases/tag/v1.2.1",
        params (string Name, string? Url)[] assets)
    {
        string notes = htmlUrl is null ? "" : $",\"html_url\":\"{htmlUrl}\"";
        string assetList = string.Join(",", assets.Select(a => a.Url is null
            ? $"{{\"name\":\"{a.Name}\"}}"
            : $"{{\"name\":\"{a.Name}\",\"browser_download_url\":\"{a.Url}\"}}"));
        string draftJson = draft ? "true" : "false";
        string prereleaseJson = prerelease ? "true" : "false";
        return $"{{\"tag_name\":\"{tag}\",\"draft\":{draftJson},\"prerelease\":{prereleaseJson}{notes},\"assets\":[{assetList}]}}";
    }

    private static (string Name, string? Url)[] AssetsFor(string version, string tag) => new (string Name, string? Url)[]
    {
        ($"FlowDesk-{version}-win-x64-setup.exe",
            $"https://github.com/kiraa000/FlowDesk/releases/download/{tag}/FlowDesk-{version}-win-x64-setup.exe"),
        ("SHA256SUMS.txt",
            $"https://github.com/kiraa000/FlowDesk/releases/download/{tag}/SHA256SUMS.txt"),
    };
}
