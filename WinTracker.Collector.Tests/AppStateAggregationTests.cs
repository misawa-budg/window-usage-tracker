using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Collector.Tests;

public sealed class AppStateAggregationTests
{
    [Theory]
    [InlineData("Open", "Minimized", "Open")]
    [InlineData("Minimized", "Open", "Open")]
    [InlineData("Active", "Open", "Active")]
    [InlineData("Open", "Active", "Active")]
    [InlineData("Minimized", "Active", "Active")]
    [InlineData("Active", "Minimized", "Active")]
    [InlineData("Minimized", "Minimized", "Minimized")]
    public void AggregationIsIndependentOfWindowEnumerationOrder(string first, string second, string expected)
    {
        var states = new Dictionary<string, AppSnapshot>(StringComparer.OrdinalIgnoreCase);
        WindowSnapshotProvider.MergeByPriority(states, new("app.exe", 1, "0x1", "first", first));
        WindowSnapshotProvider.MergeByPriority(states, new("APP.exe", 2, "0x2", "second", second));
        Assert.Equal(expected, Assert.Single(states).Value.State);

        // The viewer must resolve the same state conflict, without requiring WinUI/Win32.
        var start = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        var window = new UsageQueryWindow(start, start.AddHours(1), TimeSpan.FromMinutes(5));
        var row = Assert.Single(new TimelineLayoutBuilder().BuildDailyAppRowsFromIntervals(
            [new("app.exe", first, start, window.ToUtc), new("app.exe", second, start, window.ToUtc)], window, 960));
        Assert.StartsWith("app.exe | " + expected + " |", Assert.Single(row.Segments).Tooltip);
    }

    [Fact]
    public void DifferentAppsAreNotMerged()
    {
        var states = new Dictionary<string, AppSnapshot>(StringComparer.OrdinalIgnoreCase);
        WindowSnapshotProvider.MergeByPriority(states, new("a.exe", 1, "0x1", "", "Active"));
        WindowSnapshotProvider.MergeByPriority(states, new("b.exe", 2, "0x2", "", "Minimized"));
        Assert.Equal(2, states.Count);
        Assert.Equal("Minimized", states["b.exe"].State);
    }
}
