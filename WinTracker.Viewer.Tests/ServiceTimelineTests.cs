using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class ServiceTimelineTests
{
    [Fact]
    public void TwoBrowsersShareOneServiceRowWithoutAddingTheirParentTotals()
    {
        var start = DateTimeOffset.Parse("2026-09-20T00:00:00Z");
        var window = new UsageQueryWindow(start, start.AddHours(1), TimeSpan.FromMinutes(5));
        AppStateIntervalRow[] input = [
            new("msedge.exe", "Active", start, start.AddMinutes(10), "youtube"),
            new("chrome.exe", "Active", start.AddMinutes(10), start.AddMinutes(20), "youtube"),
            new("msedge.exe", "Active", start.AddMinutes(20), start.AddMinutes(30), "gmail"),
            new("msedge.exe", "Open", start.AddMinutes(30), start.AddHours(1))];
        var projected = BrowserServices.ProjectForeground(input);
        var builder = new TimelineLayoutBuilder();
        var rows = builder.BuildDailyAppRowsFromIntervals(projected, window, 960);
        Assert.Equal(2, rows.Count);
        var youtube = Assert.Single(rows, row => row.Label == "service:youtube");
        Assert.Equal("00:20", youtube.TotalLabel);
        Assert.StartsWith("YouTube | Active |", Assert.Single(youtube.Segments, x => !x.IsNoData).Tooltip);
        var overview = builder.BuildDailyStateStackRowsFromIntervals(projected.Select(x =>
            new ActiveIntervalRow(x.ExeName, x.StateStartUtc, x.StateEndUtc)).ToArray(), window, 960);
        Assert.Equal("00:30", Assert.Single(overview).TotalLabel);
    }
}
