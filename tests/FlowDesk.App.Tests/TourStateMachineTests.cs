using FlowDesk.App.Tour;

namespace FlowDesk.App.Tests;

public sealed class TourStateMachineTests
{
    [Fact]
    public void DefaultTour_HasSixStepsInTheDocumentedOrder()
    {
        var steps = TourStateMachine.CreateSteps();

        Assert.Equal(6, steps.Count);
        Assert.Equal(["welcome", "new-workspace", "open-workspace", "layouts", "focus", "settings"],
            steps.Select(s => s.Id));
        Assert.Equal([null, TourPage.Workspaces, TourPage.Workspaces, TourPage.Layouts, TourPage.Focus,
            TourPage.Settings], steps.Select(s => s.Page));
        Assert.Equal([null, "NewWorkspace", "OpenWorkspace", "LayoutsNav", "FocusNav", "SettingsNav"],
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
    public void Start_ShowsFirstStepAsRunning()
    {
        var tour = new TourStateMachine();

        Assert.False(tour.IsRunning);
        tour.Start();

        Assert.True(tour.IsRunning);
        Assert.Equal(0, tour.StepIndex);
        Assert.Equal(1, tour.StepNumber);
        Assert.Equal("Step 1 of 6", tour.StepLabel);
        Assert.True(tour.IsFirst);
        Assert.False(tour.IsLast);
        Assert.Equal("welcome", tour.Current.Id);
    }

    [Fact]
    public void Next_WalksAllSixSteps_ThenFinishesOnce()
    {
        var tour = new TourStateMachine();
        int finished = 0;
        tour.Finished += () => finished++;
        tour.Start();

        for (int number = 1; number <= 6; number++)
        {
            Assert.True(tour.IsRunning);
            Assert.Equal(number, tour.StepNumber);
            Assert.Equal($"Step {number} of 6", tour.StepLabel);
            if (number < 6) tour.Next();
        }

        tour.Next();

        Assert.False(tour.IsRunning);
        Assert.Equal(6, tour.StepNumber);
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
