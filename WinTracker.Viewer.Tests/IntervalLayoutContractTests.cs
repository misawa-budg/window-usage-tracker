using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class IntervalLayoutContractTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
    private readonly TimelineLayoutBuilder _builder = new(topAppCount: 2);

    [Fact]
    public void LegendPreservesRankingAndOtherColor()
    {
        ActiveIntervalRow[] rows = [new("a.exe", Start, Start.AddHours(2)),
            new("b.exe", Start.AddHours(2), Start.AddHours(3)),
            new("c.exe", Start.AddHours(3), Start.AddHours(3.5))];
        var legend = _builder.BuildOverviewLegend(rows);
        Assert.Equal(["a.exe", "b.exe", "Other"], legend.Select(x => x.Label));
        Assert.Equal(TimelineLayoutBuilder.OtherColorKey, legend[2].ColorHex);
    }

    [Fact]
    public void AppNamesSortByDurationThenName()
    {
        AppStateIntervalRow[] rows = [new("z.exe", "Active", Start, Start.AddSeconds(1000)),
            new("a.exe", "Active", Start, Start.AddSeconds(2000)),
            new("m.exe", "Open", Start, Start.AddSeconds(2000))];
        Assert.Equal(["a.exe", "m.exe", "z.exe"], _builder.BuildAppNames(rows));
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Open")]
    [InlineData("Minimized")]
    public void StateColorAndFractionalDurationDetermineSegments(string state)
    {
        var window = new UsageQueryWindow(Start, Start.AddHours(1), TimeSpan.FromMinutes(5));
        var row = Assert.Single(_builder.BuildDailyAppRowsFromIntervals(
            [new("app.exe", state, Start.AddMinutes(10), Start.AddMinutes(10.5))], window, 120));
        var segment = Assert.Single(row.Segments, x => !x.IsNoData);
        Assert.Equal(TimelinePresentation.ColorForAppState("app.exe", state), segment.ColorHex);
        Assert.Equal(1.0, segment.Width, 6);
        Assert.Equal(120.0, row.Segments.Sum(x => x.Width), 6);
    }

    [Fact]
    public void EmptyWeekKeepsSevenFullWidthNoDataRows()
    {
        var window = new UsageQueryWindow(Start, Start.AddDays(7), TimeSpan.FromMinutes(5));
        var rows = _builder.BuildWeeklyStateStackRowsFromIntervals([], window, 960);
        Assert.Equal(7, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("00:00", row.TotalLabel);
            Assert.Equal(960.0, row.Columns.Sum(x => x.Width), 6);
            Assert.All(row.Columns, column => Assert.True(column.IsNoData));
        });
    }

    [Fact]
    public void OverlappingActiveIntervalsHaveDeterministicTieBreaking()
    {
        var window = new UsageQueryWindow(Start, Start.AddHours(1), TimeSpan.FromMinutes(5));
        var row = Assert.Single(_builder.BuildDailyStateStackRowsFromIntervals(
            [new("z.exe", Start, window.ToUtc), new("a.exe", Start, window.ToUtc)], window, 960));
        Assert.Equal("a.exe", Assert.Single(Assert.Single(row.Columns).Entries).Label);
        Assert.Equal("01:00", row.TotalLabel);
    }
}
