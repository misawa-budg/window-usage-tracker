using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public class TrackHitTestTests
{
    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(0.124, 0)]
    [InlineData(0.125, 2)]
    [InlineData(9.999, 2)]
    [InlineData(10, -1)]
    [InlineData(double.NaN, -1)]
    [InlineData(double.PositiveInfinity, -1)]
    public void FindsHalfOpenIntervalsAndSkipsZeroWidth(double x, int expected)
        => Assert.Equal(expected, TrackHitTest.FindColumn([0.125, 0.125, 10], x));

    [Fact]
    public void EmptyTrackHasNoHit() => Assert.Equal(-1, TrackHitTest.FindColumn([], 0));

    [Fact]
    public void NullTrackIsRejected() => Assert.Throws<ArgumentNullException>(() => TrackHitTest.FindColumn(null!, 0));
}
