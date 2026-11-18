using FlowDesk.Core.Palette;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace FlowDesk.App.Views;

internal sealed partial class CommandPaletteWindow
{
    private readonly AiAnswerSession answer = new();
    private bool answerWasVisible;
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
            if (!e.KeyStatus.WasKeyDown && !answer.Streaming && answer.Answer.Length > 0) _ = CopyAnswerText?.Invoke(answer.Answer);
        }
        // Do not navigate the hidden result list. Native text selection/copy still gets unhandled keys.
        return true;
    }
    private void ReturnFromAnswer()
    {
        answer.Close(); SearchBox.Focus(FocusState.Programmatic); RefreshResults();
    }
    private PaletteResultRow AnswerActions()
    {
        var actions = new PaletteAction[]
        {
            new("Copy answer", () => answer.Answer.Length > 0 ? CopyAnswerText?.Invoke(answer.Answer) ?? Task.CompletedTask : Task.CompletedTask, "\uE8C8", "Enter", KeepsPaletteOpen: true),
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
            AnswerBody.Blocks.Clear();
            foreach (var block in AiMarkdown.Parse(answer.Answer))
            {
                var paragraph = new Paragraph();
                foreach (var span in block.Spans)
                {
                    var run = new Run { Text = span.Text };
                    if (span.Code) run.FontFamily = new FontFamily("Cascadia Mono");
                    paragraph.Inlines.Add(run);
                }
                AnswerBody.Blocks.Add(paragraph);
            }
            if (follow) DispatcherQueue.TryEnqueue(() => { if (answer.Visible && panel is null) AnswerScroll.ChangeView(null, AnswerScroll.ScrollableHeight, null, true); });
        }
        if (visible != answerWasVisible && IsOpen)
        {
            ResizeForResults(visible ? 8 : Results.Items.Count);
            if (visible) DispatcherQueue.TryEnqueue(() => { if (IsOpen && answer.Visible && panel is null && GetForegroundWindow() == hwnd) AnswerBody.Focus(FocusState.Programmatic); });
        }
        answerWasVisible = visible; UpdateHint();
    }
}
