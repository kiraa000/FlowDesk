using FlowDesk.App.Services;
using FlowDesk.Core.Advanced;

namespace FlowDesk.App.Tests;

public sealed class AdvancedSettingsTests
{
    [Fact] public void OldFileDefaultsOffWithNoAgreement()
    {
        var settings = AppSettings.FromJson("{\"theme\":\"Dark\"}");
        Assert.False(settings.AdvancedModeEnabled); Assert.Null(settings.AdvancedModeAgreedAt);
    }
    [Fact] public void AgreementTimeRoundTripsButDoesNotAuthorizeReenable()
    {
        var stamp = DateTimeOffset.Parse("2026-11-17T10:00:00Z");
        var settings = AppSettings.FromJson(new AppSettings { AdvancedModeEnabled = true, AdvancedModeAgreedAt = stamp }.ToJson());
        Assert.True(settings.AdvancedModeEnabled); Assert.Equal(stamp, settings.AdvancedModeAgreedAt);
        settings.AdvancedModeEnabled = false;
        settings = AppSettings.FromJson(settings.ToJson());
        Assert.Equal(stamp, settings.AdvancedModeAgreedAt);
        Assert.True(AdvancedConsent.NeedsAgreement(settings.AdvancedModeEnabled, true));
    }
}
