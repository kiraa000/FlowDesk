using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class LaunchTimeoutValueTests
{
    [Theory]
    [InlineData(20, 20)]
    [InlineData(10, 10)]
    [InlineData(60, 60)]
    [InlineData(10.4, 10)]
    [InlineData(10.6, 11)]
    [InlineData(59.5, 60)]
    [InlineData(-5, 10)]
    [InlineData(0, 10)]
    [InlineData(61, 60)]
    [InlineData(1000, 60)]
    [InlineData(double.MaxValue, 60)]
    [InlineData(double.MinValue, 10)]
    public void Normalize_ClampsAndRoundsFiniteValues(double input, int expected)
    {
        Assert.Equal(expected, LaunchTimeoutValue.Normalize(input));
    }

    [Fact]
    public void Normalize_UsesClampedFallbackForNaN()
    {
        Assert.Equal(25, LaunchTimeoutValue.Normalize(double.NaN, fallback: 25));
        Assert.Equal(LaunchTimeoutValue.Default, LaunchTimeoutValue.Normalize(double.NaN));
    }

    [Fact]
    public void Normalize_UsesClampedFallbackForInfinity()
    {
        Assert.Equal(42, LaunchTimeoutValue.Normalize(double.PositiveInfinity, fallback: 42));
        Assert.Equal(60, LaunchTimeoutValue.Normalize(double.NegativeInfinity, fallback: 999));
        Assert.Equal(10, LaunchTimeoutValue.Normalize(double.PositiveInfinity, fallback: -1));
    }

    [Fact]
    public void Normalize_ClampsAnInvalidFallback()
    {
        Assert.Equal(60, LaunchTimeoutValue.Normalize(double.NaN, fallback: int.MaxValue));
        Assert.Equal(10, LaunchTimeoutValue.Normalize(double.NaN, fallback: int.MinValue));
    }

    [Fact]
    public void Clamp_MatchesDocumentedRange()
    {
        Assert.Equal(10, LaunchTimeoutValue.Clamp(9));
        Assert.Equal(20, LaunchTimeoutValue.Clamp(20));
        Assert.Equal(60, LaunchTimeoutValue.Clamp(61));
    }
}
