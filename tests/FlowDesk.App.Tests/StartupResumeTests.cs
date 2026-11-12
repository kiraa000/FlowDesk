using FlowDesk.App.Services;
using FlowDesk.Core.Model;

namespace FlowDesk.App.Tests;

public sealed class StartupResumeTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly long[] Baseline = [11, 22];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoStoredIdDoesNothing(string? storedId)
    {
        var recorder = new Recorder();
        var outcome = await recorder.Run(storedId, new Workspace { Id = "ws", Name = "Work" });

        Assert.Equal(StartupResumeOutcome.NothingStored, outcome);
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task StoredWorkspaceIsResumedWithCurrentBaselineSettingsAndSavedTime()
    {
        var workspace = new Workspace { Id = "ws", Name = "Work", LayoutSavedAt = SavedAt };
        var recorder = new Recorder();
        var outcome = await recorder.Run("ws", workspace);

        Assert.Equal(StartupResumeOutcome.Resumed, outcome);
        var activation = Assert.Single(recorder.Activations);
        Assert.Equal("ws", activation.Id);
        Assert.Equal(Baseline, activation.Baseline);
        Assert.True(activation.Enabled);
        Assert.False(activation.Always);
        Assert.Equal(SavedAt, activation.SavedAt);
        Assert.Equal(["find:ws", "baseline", "mark:ws", "active:ws", "enabled:ws", "always:ws"], recorder.Calls);
        Assert.False(recorder.Cleared);
        Assert.Empty(recorder.Errors);
        // The helper exposes no restore, launch, place or minimize hook at all; only activation ran.
        Assert.DoesNotContain(recorder.Calls, c => c.Contains("restore") || c.Contains("launch") || c.Contains("place"));
    }

    [Fact]
    public async Task MissingOrDeletedStoredWorkspaceIsClearedSilently()
    {
        var recorder = new Recorder();
        var outcome = await recorder.Run("gone", null);

        Assert.Equal(StartupResumeOutcome.ClearedMissing, outcome);
        Assert.True(recorder.Cleared);
        Assert.Equal(["find:gone", "clear"], recorder.Calls);
        Assert.Empty(recorder.Activations);
        Assert.Empty(recorder.Errors);
    }

    [Fact]
    public async Task BaselineFailureIsReportedWithoutActivatingOrClearingTheStoredId()
    {
        var workspace = new Workspace { Id = "ws", Name = "Work" };
        var recorder = new Recorder { ThrowFromBaseline = true };
        var outcome = await recorder.Run("ws", workspace);

        Assert.Equal(StartupResumeOutcome.Failed, outcome);
        Assert.Single(recorder.Errors);
        Assert.Contains("Work", recorder.Errors[0]);
        Assert.Contains("baseline failed", recorder.Errors[0]);
        Assert.Empty(recorder.Activations);
        Assert.False(recorder.Cleared);
    }

    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];
        public List<string> Errors { get; } = [];
        public List<Activation> Activations { get; } = [];
        public bool Cleared { get; private set; }
        public bool ThrowFromBaseline { get; init; }

        public Task<StartupResumeOutcome> Run(string? storedId, Workspace? workspace) =>
            StartupResume.TryResumeAsync(
                storedId,
                id => { Calls.Add("find:" + id); return workspace?.Id == id ? workspace : null; },
                () =>
                {
                    Calls.Add("baseline");
                    return ThrowFromBaseline
                        ? throw new IOException("baseline failed")
                        : Task.FromResult<IReadOnlyList<long>>(Baseline);
                },
                id => { Calls.Add("enabled:" + id); return true; },
                id => { Calls.Add("always:" + id); return false; },
                id => Calls.Add("mark:" + id),
                id => Calls.Add("active:" + id),
                (id, baseline, enabled, always, savedAt) =>
                    Activations.Add(new(id, baseline.ToArray(), enabled, always, savedAt)),
                () => { Calls.Add("clear"); Cleared = true; },
                message => Errors.Add(message));
    }

    private sealed record Activation(
        string Id, IReadOnlyList<long> Baseline, bool Enabled, bool Always, DateTimeOffset? SavedAt);
}
