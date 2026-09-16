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
}
