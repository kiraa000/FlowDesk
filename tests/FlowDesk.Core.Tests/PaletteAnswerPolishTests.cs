using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteAnswerPolishTests
{
    [Fact] public void OneLineAnswerIsCompactAndLongAnswersGrowOnlyToTheExistingMaximum()
    {
        Assert.Equal(176, PaletteAnswerLayout.Height(20, 20, 0));
        Assert.True(PaletteAnswerLayout.Height(20, 160, 0) > PaletteAnswerLayout.Height(20, 20, 0));
        Assert.Equal(564, PaletteAnswerLayout.Height(20, 10000, 16));
        Assert.True(PaletteAnswerLayout.Height(40, 80, 32, 80, 44) > PaletteAnswerLayout.Height(20, 40, 16));
        Assert.InRange(PaletteAnswerLayout.Height(double.NaN, double.PositiveInfinity, 0), 152, 564);
    }
    [Theory] [InlineData('a')] [InlineData('A')] [InlineData(' ')] [InlineData('?')] [InlineData('é')] [InlineData('中')]
    public void PrintableComposedTextStartsANewSearch(char character) => Assert.True(PaletteAnswerInput.StartsSearch(character, false, false));
    [Fact] public void ControlSelectionCopyAndMenuShortcutsStayNativeButAltGrProducesText()
    {
        Assert.False(PaletteAnswerInput.StartsSearch('a', true, false)); Assert.False(PaletteAnswerInput.StartsSearch('c', true, false));
        Assert.False(PaletteAnswerInput.StartsSearch('\u0003', true, false)); Assert.False(PaletteAnswerInput.StartsSearch('\r', false, false));
        Assert.False(PaletteAnswerInput.StartsSearch('f', false, true)); Assert.True(PaletteAnswerInput.StartsSearch('@', true, true, true));
    }
    [Fact] public async Task StatusTextDoesNotDuplicateFooterInstructions()
    {
        var session = new AiAnswerSession();
        await session.AskAsync(new OneLineProvider(), "q"); Assert.Equal("", session.Status);
        var pending = new PendingProvider(); Task request = session.AskAsync(pending, "q");
        Assert.Equal("Answering…", session.Status); session.Cancel(); Assert.Equal("Cancelled", session.Status);
        pending.Done.SetResult(); await request;
    }
    private sealed class OneLineProvider : IAiAnswerProvider
    {
        public async IAsyncEnumerable<AiChunk> StreamAsync(string question, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { await Task.Yield(); yield return new("answer"); }
    }
    private sealed class PendingProvider : IAiAnswerProvider
    {
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<AiChunk> StreamAsync(string question, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { await Done.Task; yield return new("late"); }
    }
}
