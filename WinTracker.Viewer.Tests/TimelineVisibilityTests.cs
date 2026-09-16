using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class TimelineVisibilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShortDailyUsageRemainsVisibleThroughoutTheWeek()
    {
        var builder = new TimelineLayoutBuilder();
        var intervals = Enumerable.Range(0, 7).Select(day =>
            new ActiveIntervalRow("editor.exe", Start.AddDays(day), Start.AddDays(day).AddMinutes(4))).ToArray();
        var window = new UsageQueryWindow(Start, Start.AddDays(7), TimeSpan.FromMinutes(5));
        Assert.Single(builder.BuildOverviewLegend(intervals));
        Assert.All(builder.BuildWeeklyStateStackRowsFromIntervals(intervals, window, 960), row =>
        {
            Assert.Equal("00:04", row.TotalLabel);
            Assert.Contains(row.Columns, column => !column.IsNoData);
        });
    }

    [Fact]
    public void OtherColorMatchesLegendAndNinthAppIsNotDropped()
    {
        var builder = new TimelineLayoutBuilder();
        var active = Enumerable.Range(0, 9).Select(index =>
            new ActiveIntervalRow($"app{index}.exe", Start.AddMinutes(index * 6), Start.AddMinutes((index + 1) * 6))).ToArray();
        var states = active.Select(x => new AppStateIntervalRow(x.ExeName, "Active", x.StateStartUtc, x.StateEndUtc)).ToArray();
        var window = new UsageQueryWindow(Start, Start.AddDays(1), TimeSpan.FromMinutes(5));
        var legend = builder.BuildOverviewLegend(active);
        Assert.Equal(TimelineLayoutBuilder.OtherColorKey, legend.Last().ColorHex);
        var overview = Assert.Single(builder.BuildDailyStateStackRowsFromIntervals(active, window, 960));
        var ninth = overview.Columns.SelectMany(x => x.Entries).Single(x => x.Label == "app8.exe");
        Assert.Equal(TimelineLayoutBuilder.OtherColorKey, ninth.ColorHex);
        Assert.Equal(9, builder.BuildDailyAppRowsFromIntervals(states, window, 960).Count);
    }

    [Fact]
    public void ManyAdjacentCheckpointsMergeIntoOneVisibleSegment()
    {
        var builder = new TimelineLayoutBuilder();
        var intervals = Enumerable.Range(0, 10000).Select(index =>
            new AppStateIntervalRow("editor.exe", "Active", Start.AddSeconds(index * 15), Start.AddSeconds((index + 1) * 15))).ToArray();
        var window = new UsageQueryWindow(Start, Start.AddSeconds(150000), TimeSpan.FromMinutes(5));
        var row = Assert.Single(builder.BuildDailyAppRowsFromIntervals(intervals, window, 960));
        var segment = Assert.Single(row.Segments);
        Assert.False(segment.IsNoData);
        Assert.InRange(segment.Width, 959.999, 960.001);
    }

    [Fact]
    public void SweepMatchesReferenceForOverlapsAndTies()
    {
        var random = new Random(42);
        var builder = new TimelineLayoutBuilder();
        var window = new UsageQueryWindow(Start, Start.AddSeconds(120), TimeSpan.FromSeconds(1));
        var intervals = Enumerable.Range(0, 200).Select(index =>
        {
            int from = random.Next(-10, 110);
            return new ActiveIntervalRow($"app{index % 3}.exe", Start.AddSeconds(from), Start.AddSeconds(from + random.Next(1, 25)));
        }).ToArray();
        var row = Assert.Single(builder.BuildDailyStateStackRowsFromIntervals(intervals, window, 120));
        for (int appIndex = 0; appIndex < 3; appIndex++)
        {
            string app = $"app{appIndex}.exe";
            int expected = Enumerable.Range(0, 120).Count(second => intervals
                .Where(x => x.StateStartUtc <= Start.AddSeconds(second) && x.StateEndUtc > Start.AddSeconds(second))
                .OrderByDescending(x => x.StateStartUtc < Start ? Start : x.StateStartUtc)
                .ThenBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.ExeName).FirstOrDefault() == app);
            double actual = row.Columns.Where(x => x.Entries.Any(entry => entry.Label == app)).Sum(x => x.Width);
            Assert.InRange(actual, expected - 0.0001, expected + 0.0001);
        }
    }
}
