using FlowDesk.Core;

namespace FlowDesk.Core.Tests;

public sealed class CountTextTests
{
    [Theory]
    [InlineData(0, "0 items")]
    [InlineData(1, "1 item")]
    [InlineData(2, "2 items")]
    [InlineData(-1, "-1 items")]
    public void CountsUseSingularOnlyForOne(long count, string expected) =>
        Assert.Equal(expected, CountText.Format(count, "item"));

    [Fact] public void SupportsIrregularPluralsAndLargeCounts()
    {
        Assert.Equal("2 entries", CountText.Format(2, "entry", "entries"));
        Assert.Equal("5000000000 bytes", CountText.Format(5_000_000_000, "byte"));
        Assert.Equal("100+ items", CountText.Format(100, "item", suffix: "+"));
    }
}
