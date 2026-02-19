using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class TimelineLayoutBuilderTests
{
    [Fact]
    public void BuildDailyOverviewLanes_ReturnsThreeLanes_WithFullTrackWidth()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = Create24HourWindow(Utc(2026, 2, 19, 0, 0, 0));
        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 19, 9, "devenv.exe", "Active", 3600),
            Row(2026, 2, 19, 10, "msedge.exe", "Open", 1800),
            Row(2026, 2, 19, 10, "powershell.exe", "Open", 1800),
            Row(2026, 2, 19, 11, "msedge.exe", "Minimized", 1200)
        ];

        IReadOnlyList<StateLaneLayout> lanes = builder.BuildDailyOverviewLanes(rows, window, trackWidth: 960);

        Assert.Equal(3, lanes.Count);
        Assert.Contains(lanes, x => x.Label == "Active");
        Assert.Contains(lanes, x => x.Label == "Open");
        Assert.Contains(lanes, x => x.Label == "Minimized");

        foreach (StateLaneLayout lane in lanes)
        {
            AssertApproximately(lane.Segments.Sum(x => x.Width), 960);
            Assert.True(ParseDuration(lane.TotalLabel) <= TimeSpan.FromHours(24));
        }
    }

    [Fact]
    public void BuildOverviewRows_ForWeek_ReturnsSevenDayRows_WithCappedTotals()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = CreateWeekWindow(Utc(2026, 2, 13, 0, 0, 0));

        var rows = new List<TimelineUsageRow>();
        for (int day = 0; day < 7; day++)
        {
            DateTimeOffset d = Utc(2026, 2, 13 + day, 0, 0, 0);
            rows.Add(new TimelineUsageRow(d.AddHours(9), "devenv.exe", "Active", 3600));
            rows.Add(new TimelineUsageRow(d.AddHours(9), "msedge.exe", "Open", 1800));
            rows.Add(new TimelineUsageRow(d.AddHours(9), "powershell.exe", "Open", 1800));
            rows.Add(new TimelineUsageRow(d.AddHours(14), "msedge.exe", "Minimized", 1200));
        }

        IReadOnlyList<TimelineRowLayout> result = builder.BuildOverviewRows(rows, window, trackWidth: 760);

        Assert.Equal(7, result.Count);
        foreach (TimelineRowLayout row in result)
        {
            Assert.NotEmpty(row.BucketLabel);
            AssertApproximately(row.Segments.Sum(x => x.Width), 760);
            Assert.True(ParseDuration(row.TotalLabel) <= TimeSpan.FromHours(24));
        }
    }

    [Fact]
    public void BuildAppTimelineRows_UsesStateColors_AndKeepsTrackWidth()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = CreateWeekWindow(Utc(2026, 2, 13, 0, 0, 0));

        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 13, 8, "devenv.exe", "Active", 3600),
            Row(2026, 2, 13, 8, "devenv.exe", "Open", 1800),
            Row(2026, 2, 13, 8, "devenv.exe", "Minimized", 600),
            Row(2026, 2, 14, 9, "devenv.exe", "Open", 3600),
            Row(2026, 2, 14, 10, "devenv.exe", "Active", 2400)
        ];

        IReadOnlyList<TimelineRowLayout> result = builder.BuildAppTimelineRows(rows, window, "devenv.exe", trackWidth: 760);

        Assert.Equal(7, result.Count);
        HashSet<string> expectedColors =
        [
            TimelineLayoutBuilder.ActiveColorHex,
            TimelineLayoutBuilder.OpenColorHex,
            TimelineLayoutBuilder.MinimizedColorHex
        ];

        foreach (TimelineRowLayout row in result)
        {
            AssertApproximately(row.Segments.Sum(x => x.Width), 760);
            Assert.True(ParseDuration(row.TotalLabel) <= TimeSpan.FromHours(24));

            foreach (SegmentLayout segment in row.Segments.Where(x => !x.IsNoData))
            {
                Assert.Contains(segment.ColorHex, expectedColors);
            }
        }
    }

    [Fact]
    public void BuildAppDailyLanes_UsesSingleAppColorAcrossStates()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = Create24HourWindow(Utc(2026, 2, 19, 0, 0, 0));
        string app = "devenv.exe";

        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 19, 8, app, "Active", 1800),
            Row(2026, 2, 19, 9, app, "Open", 2400),
            Row(2026, 2, 19, 10, app, "Minimized", 1200)
        ];

        IReadOnlyList<StateLaneLayout> lanes = builder.BuildAppDailyLanes(rows, window, app, trackWidth: 960);
        string expectedColor = TimelineLayoutBuilder.ColorForKey(app);

        foreach (StateLaneLayout lane in lanes)
        {
            AssertApproximately(lane.Segments.Sum(x => x.Width), 960);
            foreach (SegmentLayout segment in lane.Segments.Where(x => !x.IsNoData))
            {
                Assert.Equal(expectedColor, segment.ColorHex);
            }
        }
    }

    [Fact]
    public void BuildOverviewLegend_AppendsOther_WhenTopCountExceeded()
    {
        var builder = new TimelineLayoutBuilder(topAppCount: 2);
        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 19, 0, "a.exe", "Active", 7200),
            Row(2026, 2, 19, 1, "b.exe", "Active", 3600),
            Row(2026, 2, 19, 2, "c.exe", "Active", 1800)
        ];

        IReadOnlyList<LegendItemLayout> legend = builder.BuildOverviewLegend(rows);

        Assert.Equal(3, legend.Count);
        Assert.Equal("a.exe", legend[0].Label);
        Assert.Equal("b.exe", legend[1].Label);
        Assert.Equal(TimelineLayoutBuilder.OtherLabel, legend[2].Label);
        Assert.Equal(TimelineLayoutBuilder.OtherColorHex, legend[2].ColorHex);
    }

    [Fact]
    public void BuildAppNames_SortsBySecondsDesc_ThenName()
    {
        var builder = new TimelineLayoutBuilder();
        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 19, 0, "z.exe", "Active", 1000),
            Row(2026, 2, 19, 0, "a.exe", "Active", 2000),
            Row(2026, 2, 19, 1, "m.exe", "Open", 2000)
        ];

        IReadOnlyList<string> names = builder.BuildAppNames(rows);
        Assert.Equal(["a.exe", "m.exe", "z.exe"], names);
    }

    private static TimelineUsageRow Row(
        int year,
        int month,
        int day,
        int hour,
        string exeName,
        string state,
        double seconds) =>
        new(Utc(year, month, day, hour, 0, 0), exeName, state, seconds);

    private static UsageQueryWindow Create24HourWindow(DateTimeOffset dayStartUtc) =>
        new(dayStartUtc, dayStartUtc.AddDays(1), TimeSpan.FromHours(1));

    private static UsageQueryWindow CreateWeekWindow(DateTimeOffset weekStartUtc) =>
        new(weekStartUtc, weekStartUtc.AddDays(7), TimeSpan.FromHours(1));

    private static DateTimeOffset Utc(int y, int m, int d, int hh, int mm, int ss) =>
        new(y, m, d, hh, mm, ss, TimeSpan.Zero);

    private static TimeSpan ParseDuration(string value)
    {
        string[] parts = value.Split(':');
        return new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), 0);
    }

    private static void AssertApproximately(double actual, double expected)
    {
        const double tolerance = 0.001;
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
}
