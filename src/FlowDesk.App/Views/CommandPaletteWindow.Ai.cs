using FlowDesk.Core.Palette;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.Foundation;

namespace FlowDesk.App.Views;

internal sealed partial class CommandPaletteWindow
{
    private readonly AiAnswerSession answer = new();
    private bool answerWasVisible;
    private long answerFocusVersion;
    private readonly DispatcherTimer answerRenderTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    public Func<string, Task>? CopyAnswerText { get; set; }
    public Func<string, Task>? AskAgain { get; set; }
    public Func<Task>? OpenAiChat { get; set; }
    private void QueueAnswerRender()
    {
        if (!answer.Streaming || !answer.Visible || !answerWasVisible) { answerRenderTimer.Stop(); RenderAnswer(); }
        else if (!answerRenderTimer.IsEnabled) answerRenderTimer.Start();
    }
    public Task ShowAnswerAsync(IAiAnswerProvider provider, string question)
    {
        if (!IsOpen) return Task.CompletedTask;
        answerFocusVersion++;
        ClosePreview?.Invoke(); CancelConfirmation(); CloseActions(); query?.Cancel(); generation++;
        _ = LoadAnswerAsync(provider, question);
        return Task.CompletedTask; // the launcher execution gate must not wait for network I/O
    }
    private async Task LoadAnswerAsync(IAiAnswerProvider provider, string question)
    {
        try { await answer.AskAsync(provider, question); }
        catch (Exception) { if (IsOpen) SetStatus("The answer could not be displayed. Press Esc to return."); }
    }
    private bool HandleAnswerKey(KeyRoutedEventArgs e, bool ctrl)
    {
        if (!answer.Visible || panel is not null || (ctrl && e.Key == VirtualKey.K)) return false;
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            if (answer.Streaming) answer.Cancel(); else ReturnFromAnswer();
        }
        else if (e.Key == VirtualKey.Enter && FocusManager.GetFocusedElement(Root.XamlRoot) is not Button)
        {
            e.Handled = true;
            if (!e.KeyStatus.WasKeyDown && !answer.Streaming && answer.Answer.Length > 0) _ = CopyAnswerAsync();
        }
        // Do not navigate the hidden result list. Native text selection/copy still gets unhandled keys.
        return true;
    }
    private void ReturnFromAnswer()
    {
        answerFocusVersion++;
        answer.Close(); SearchBox.Focus(FocusState.Programmatic); RefreshResults();
    }
    private async Task CopyAnswerAsync()
    {
        if (answer.Answer.Length == 0) return;
        answerFocusVersion++;
        if (CopyAnswerText is not null) await CopyAnswerText(answer.Answer);
        if (IsOpen && answer.Visible && GetForegroundWindow() == hwnd) SearchBox.Focus(FocusState.Programmatic);
    }
    private void Answer_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (!IsOpen || !answer.Visible || panel is not null) return;
        bool ctrl = (GetKeyState(0x11) & 0x8000) != 0, alt = (GetKeyState(0x12) & 0x8000) != 0;
        bool altGr = ctrl && (GetKeyState(0xA5) & 0x8000) != 0;
        if (!PaletteAnswerInput.StartsSearch(e.Character, ctrl, alt, altGr)) return;
        answerFocusVersion++;
        e.Handled = true;
        string text = e.Character.ToString();
        answer.Close(); // cancels streaming and restores the editable search before assigning text
        bool same = SearchBox.Text == text;
        SearchBox.Text = text; SearchBox.SelectionStart = text.Length; SearchBox.SelectionLength = 0;
        SearchBox.Focus(FocusState.Programmatic);
        if (same) RefreshResults();
    }
    private PaletteResultRow AnswerActions()
    {
        var actions = new PaletteAction[]
        {
            new("Copy answer", CopyAnswerAsync, "\uE8C8", "Enter", KeepsPaletteOpen: true),
            new("Ask again", () => AskAgain?.Invoke(answer.Question) ?? Task.CompletedTask, "\uE72C", KeepsPaletteOpen: true, RequiresEnter: true),
            new("Open in browser chat", () => OpenAiChat?.Invoke() ?? Task.CompletedTask, "\uE774", KeepsPaletteOpen: true),
            new("Copy question", () => CopyAnswerText?.Invoke(answer.Question) ?? Task.CompletedTask, "\uE8C8", KeepsPaletteOpen: true)
        };
        return new(new(new("ai:answer", "AI answer", "", CommandKind.Ai, "\uE945", actions[0].Run, NamedActions: actions, RecordUsage: false), 1, []));
    }
    private void RenderAnswer()
    {
        bool visible = answer.Visible;
        AnswerPane.Visibility = visible && panel is null ? Visibility.Visible : Visibility.Collapsed;
        Results.Visibility = visible && panel is null ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.IsReadOnly = visible;
        if (visible && panel is null)
        {
            bool follow = AnswerScroll.ScrollableHeight - AnswerScroll.VerticalOffset < 24;
            AnswerQuestion.Text = answer.Question;
            if (AnswerStatus.Text != answer.Status)
            {
                AnswerStatus.Text = answer.Status; SetAccessibleName(AnswerStatus, answer.Status);
                FrameworkElementAutomationPeer.CreatePeerForElement(AnswerStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            AnswerStatus.Visibility = answer.Status.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            AnswerPane.RowSpacing = 0;
            AnswerQuestion.Margin = new Thickness(0, 0, 0, 8);
            AnswerStatus.Margin = new Thickness(0, 8, 0, 0);
            AnswerBody.Blocks.Clear();
            foreach (var block in AiMarkdown.Parse(answer.Answer))
            {
                var paragraph = new Paragraph();
                // WinUI Paragraph has no margin: one blank body line separates semantic blocks.
                if (AnswerBody.Blocks.Count > 0) paragraph.Inlines.Add(new LineBreak());
                foreach (var span in block.Spans)
                {
                    var run = new Run { Text = span.Text };
                    if (span.Code) run.FontFamily = new FontFamily("Cascadia Mono");
                    if (span.Bold) run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    if (span.Italic) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
                    paragraph.Inlines.Add(run);
                }
                AnswerBody.Blocks.Add(paragraph);
            }
            ResizeForAnswer();
            if (follow) DispatcherQueue.TryEnqueue(() => { if (answer.Visible && panel is null) AnswerScroll.ChangeView(null, AnswerScroll.ScrollableHeight, null, true); });
        }
        if (visible != answerWasVisible && IsOpen)
        {
            if (!visible) ResizeForResults(Results.Items.Count);
            if (visible)
            {
                long scheduledFocus = answerFocusVersion;
                DispatcherQueue.TryEnqueue(() => { if (scheduledFocus == answerFocusVersion && IsOpen && answer.Visible && panel is null && GetForegroundWindow() == hwnd) AnswerBody.Focus(FocusState.Programmatic); });
            }
        }
        answerWasVisible = visible; UpdateHint();
    }
    private void ResizeForAnswer()
    {
        if (!IsOpen) return;
        double width = Math.Max(1, (Root.ActualWidth > 0 ? Root.ActualWidth : 640) - 26 - AnswerPane.Padding.Left - AnswerPane.Padding.Right);
        var available = new Size(width, double.PositiveInfinity);
        AnswerQuestion.Measure(available); AnswerBody.Measure(available);
        if (AnswerStatus.Visibility == Visibility.Visible) AnswerStatus.Measure(available);
        static double ContentHeight(FrameworkElement element) => Math.Max(0, element.DesiredSize.Height - element.Margin.Top - element.Margin.Bottom);
        ResizeForHeight(PaletteAnswerLayout.Height(ContentHeight(AnswerQuestion), ContentHeight(AnswerBody),
            AnswerStatus.Visibility == Visibility.Visible ? ContentHeight(AnswerStatus) : 0, SearchBox.ActualHeight, ActionsButton.ActualHeight));
    }
}
