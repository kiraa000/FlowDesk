// Onboarding tour: the six steps and the movement rules, with no WinUI dependency.
// The shell owns the TeachingTip and page navigation; this class only decides where the tour is.
namespace FlowDesk.App.Tour;

/// <summary>The page a tour step needs before its target exists.</summary>
public enum TourPage { Workspaces, Layouts, Focus, Settings }

/// <summary>
/// One teaching tip step. A null <see cref="TargetId"/> means a centred tip;
/// <see cref="FallbackBody"/> is used when the target is the empty-state button instead.
/// </summary>
public sealed record TourStep(string Id, string Title, string Body, TourPage? Page, string? TargetId,
    string? FallbackBody = null);

/// <summary>
/// Plain C# tour state machine: Start, Next, Back and Skip/Finish. Back stops at the first step,
/// Next finishes on the last step, and both skipping and finishing raise <see cref="Finished"/>.
/// </summary>
public sealed class TourStateMachine
{
    public static IReadOnlyList<TourStep> CreateSteps() =>
    [
        new("welcome", "Welcome to FlowDesk",
            "FlowDesk saves your open apps, files and websites as a workspace and brings them back in one click. " +
            "This short tour shows the four places you will use.", null, null),
        new("new-workspace", "Save what is open",
            "Start here: save the windows you have open now as a new workspace.", TourPage.Workspaces, "NewWorkspace"),
        new("open-workspace", "Open a workspace",
            "Open relaunches the workspace's apps and puts every window back where it was. " +
            "Switch between workspaces as often as you like.", TourPage.Workspaces, "OpenWorkspace",
            "No workspace exists yet. Save the windows you have open here; Open will bring them back later."),
        new("layouts", "Arrange windows",
            "Layouts arranges the open windows on the selected monitor: two columns, a sidebar, a grid or a cascade.",
            TourPage.Layouts, "LayoutsNav"),
        new("focus", "Focus sessions",
            "Focus closes distracting apps for 25, 50 or 90 minutes while keeping your allowed apps open.",
            TourPage.Focus, "FocusNav"),
        new("settings", "Make it yours",
            "Settings has the theme, launch timeout, start with Windows, the data folder and this tour. " +
            "You can replay it from here at any time.", TourPage.Settings, null),
    ];

    private readonly List<TourStep> steps;

    public TourStateMachine() : this(CreateSteps()) { }

    public TourStateMachine(IReadOnlyList<TourStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0) throw new ArgumentException("A tour needs at least one step.", nameof(steps));
        this.steps = [.. steps];
    }

    /// <summary>Raised whenever the current step or the running state changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once when the tour is finished or skipped.</summary>
    public event Action? Finished;

    public IReadOnlyList<TourStep> Steps => steps;
    public int TotalSteps => steps.Count;
    public int StepIndex { get; private set; }
    public int StepNumber => StepIndex + 1;
    public TourStep Current => steps[StepIndex];
    public bool IsRunning { get; private set; }
    public bool IsFirst => StepIndex == 0;
    public bool IsLast => StepIndex == steps.Count - 1;
    public string StepLabel => $"Step {StepNumber} of {TotalSteps}";

    /// <summary>Shows the first step (also restarts a finished or running tour).</summary>
    public void Start()
    {
        StepIndex = 0;
        IsRunning = true;
        Changed?.Invoke();
    }

    public void Next()
    {
        if (!IsRunning) return;
        if (IsLast) Complete();
        else { StepIndex++; Changed?.Invoke(); }
    }

    public void Back()
    {
        if (!IsRunning || IsFirst) return;
        StepIndex--;
        Changed?.Invoke();
    }

    /// <summary>Ends the tour early; the caller persists <c>TourCompleted</c> on <see cref="Finished"/>.</summary>
    public void Skip() => Complete();

    private void Complete()
    {
        if (!IsRunning) return;
        IsRunning = false;
        Changed?.Invoke();
        Finished?.Invoke();
    }
}
