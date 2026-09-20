using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class TimelinePresentationTests
{
    [Theory]
    [InlineData(-1, "00:00", "00:00:00")]
    [InlineData(59.6, "00:01", "00:01:00")]
    [InlineData(90061, "25:01", "25:01:01")]
    public void DurationsRetainRoundingAndHoursBeyondOneDay(double seconds, string minutes, string full)
    {
        Assert.Equal(minutes, TimelinePresentation.ToDuration(seconds));
        Assert.Equal(full, TimelinePresentation.ToDurationWithSeconds(seconds));
    }

    [Fact]
    public void MidnightEndpointIs24ButZeroDurationRemains00()
    {
        var midnight = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal("23:59:59-24:00:00", TimelinePresentation.FormatTooltipTimeRange(midnight.AddSeconds(-1), midnight));
        Assert.Equal("00:00:00-00:00:00", TimelinePresentation.FormatTooltipTimeRange(midnight, midnight));
    }

    [Fact]
    public void AppColorsIgnoreCaseAndStateTonesStayDistinct()
    {
        string active = TimelinePresentation.ColorForKey("Code.exe");
        Assert.Equal(active, TimelinePresentation.ColorForKey("CODE.EXE"));
        Assert.Equal(active, TimelinePresentation.ColorForAppState("Code.exe", "Active"));
        Assert.Equal(active, TimelinePresentation.ColorForAppState("Code.exe", "Running"));
        string open = TimelinePresentation.ColorForAppState("Code.exe", "Open");
        string minimized = TimelinePresentation.ColorForAppState("Code.exe", "Minimized");
        Assert.Equal(3, new[] { active, open, minimized }.Distinct().Count());
        Assert.All(new[] { active, open, minimized }, color => Assert.Matches("^#[0-9A-F]{6}$", color));
    }
}
