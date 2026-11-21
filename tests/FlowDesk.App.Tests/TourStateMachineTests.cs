using FlowDesk.App.Tour;

namespace FlowDesk.App.Tests;

public sealed class TourStateMachineTests
{
    [Fact]
    public void DefaultTour_HasEightStepsInTheDocumentedOrder()
    {
        var steps = TourStateMachine.CreateSteps();

        Assert.Equal(8, steps.Count);
        Assert.Equal(["welcome", "new-workspace", "open-workspace", "layouts", "focus", "palette", "quick-look-calm", "settings"],
            steps.Select(s => s.Id));
        Assert.Equal([null, TourPage.Workspaces, TourPage.Workspaces, TourPage.Layouts, TourPage.Focus,
            TourPage.Settings, TourPage.Settings, TourPage.Settings], steps.Select(s => s.Page));
        Assert.Equal([null, "NewWorkspace", "OpenWorkspace", "LayoutsNav", "FocusNav", "PaletteOptions", "CalmNav", null],
            steps.Select(s => s.TargetId));
        Assert.All(steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Body));
        });
        Assert.Null(steps[0].FallbackBody);
        Assert.False(string.IsNullOrWhiteSpace(steps[2].FallbackBody));
    }

    [Fact]
    public void NewSteps_CoverPaletteAndQuickLookHotCornersCalm()
    {
        var steps = TourStateMachine.CreateSteps();

        var palette = Assert.Single(steps, step => step.Id == "palette");
        Assert.Equal(TourPage.Settings, palette.Page);
        Assert.Equal("PaletteOptions", palette.TargetId);
        Assert.Contains("prefix", palette.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("available prefixes", palette.Body, StringComparison.OrdinalIgnoreCase);

        var combined = Assert.Single(steps, step => step.Id == "quick-look-calm");
        Assert.Equal(TourPage.Settings, combined.Page);
        Assert.Equal("CalmNav", combined.TargetId);
        Assert.Contains("Quick Look", combined.Body, StringComparison.Ordinal);
        Assert.Contains("Hot corners", combined.Body, StringComparison.Ordinal);
        Assert.Contains("Calm", combined.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsStep_IsCentredOnTheSettingsPage()
    {
        var steps = TourStateMachine.CreateSteps();

        // The Settings nav item lives in the NavigationView footer, where an anchored tip can be placed
        // off-screen. The final step therefore uses the same centred, targetless presentation as welcome.
        var settings = steps[^1];
        Assert.Equal(TourPage.Settings, settings.Page);
        Assert.Null(settings.TargetId);
    }

    [Fact]
    public void Start_ShowsFirstStepAsRunning()
    {
        var tour = new TourStateMachine();

        Assert.False(tour.IsRunning);
        tour.Start();

        Assert.True(tour.IsRunning);
        Assert.Equal(0, tour.StepIndex);
        Assert.Equal(1, tour.StepNumber);
        Assert.Equal("Step 1 of 8", tour.StepLabel);
        Assert.True(tour.IsFirst);
        Assert.False(tour.IsLast);
        Assert.Equal("welcome", tour.Current.Id);
    }

    [Fact]
    public void Next_WalksAllEightSteps_ThenFinishesOnce()
    {
        var tour = new TourStateMachine();
        int finished = 0;
        tour.Finished += () => finished++;
        tour.Start();

        for (int number = 1; number <= 8; number++)
        {
            Assert.True(tour.IsRunning);
            Assert.Equal(number, tour.StepNumber);
            Assert.Equal($"Step {number} of 8", tour.StepLabel);
            if (number < 8) tour.Next();
        }

        tour.Next();

        Assert.False(tour.IsRunning);
        Assert.Equal(8, tour.StepNumber);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void Back_ReturnsToThePreviousStep_AndStopsAtTheFirst()
    {
        var tour = new TourStateMachine();
        tour.Start();
        tour.Next();
        tour.Next();

        tour.Back();

        Assert.Equal(1, tour.StepIndex);
        Assert.Equal("new-workspace", tour.Current.Id);
        tour.Back();
        tour.Back();
        Assert.Equal(0, tour.StepIndex);
        Assert.True(tour.IsFirst);
    }

    [Fact]
    public void Skip_StopsTheTour_AndRaisesFinishedOnlyOnce()
    {
        var tour = new TourStateMachine();
        int finished = 0;
        tour.Finished += () => finished++;
        tour.Start();
        tour.Next();

        tour.Skip();

        Assert.False(tour.IsRunning);
        Assert.Equal(1, finished);
        int index = tour.StepIndex;
        tour.Skip();
        tour.Next();
        tour.Back();
        Assert.Equal(1, finished);
        Assert.Equal(index, tour.StepIndex);
    }

    [Fact]
    public void Start_AfterFinishing_RestartsAtTheFirstStep()
    {
        var tour = new TourStateMachine();
        tour.Start();
        tour.Next();
        tour.Skip();

        tour.Start();

        Assert.True(tour.IsRunning);
        Assert.Equal(0, tour.StepIndex);
        Assert.Equal("welcome", tour.Current.Id);
    }

    [Fact]
    public void Changed_RaisedForStartMovementAndFinish()
    {
        var tour = new TourStateMachine();
        int changed = 0;
        tour.Changed += () => changed++;

        tour.Start();
        tour.Next();
        tour.Back();
        tour.Skip();

        Assert.Equal(4, changed);
    }

    [Fact]
    public void Constructor_RejectsAnEmptyStepList()
    {
        Assert.Throws<ArgumentException>(() => new TourStateMachine([]));
    }
}
