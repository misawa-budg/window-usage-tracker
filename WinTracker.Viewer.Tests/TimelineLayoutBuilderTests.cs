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
    public void BuildDailyStateGroups_ReturnsThreeGroups_WithTopAppLanes()
    {
        var builder = new TimelineLayoutBuilder(topAppCount: 2);
        UsageQueryWindow window = Create24HourWindow(Utc(2026, 2, 19, 0, 0, 0));
        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 19, 9, "devenv.exe", "Active", 3600),
            Row(2026, 2, 19, 10, "msedge.exe", "Active", 1800),
            Row(2026, 2, 19, 10, "powershell.exe", "Active", 900),
            Row(2026, 2, 19, 11, "msedge.exe", "Open", 1200),
            Row(2026, 2, 19, 12, "powershell.exe", "Minimized", 2400)
        ];

        IReadOnlyList<StateGroupLayout> groups = builder.BuildDailyStateGroups(rows, window, trackWidth: 960);

        Assert.Equal(3, groups.Count);
        foreach (StateGroupLayout group in groups)
        {
            Assert.NotEmpty(group.Label);
            Assert.True(ParseDuration(group.TotalLabel) >= TimeSpan.Zero);
            foreach (StateLaneLayout lane in group.Lanes)
            {
                AssertApproximately(lane.Segments.Sum(x => x.Width), 960);
            }
        }
    }

    [Fact]
    public void BuildDailyStateStackRows_ReturnsSingleRunningRow_AndSplitsConcurrentApps()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 20, 0, 0, 0),
            TimeSpan.FromMinutes(1));

        IReadOnlyList<TimelineUsageRow> rows =
        [
            new(Utc(2026, 2, 19, 0, 10, 0), "devenv.exe", "Active", 300),
            new(Utc(2026, 2, 19, 0, 10, 0), "powershell.exe", "Active", 300),
            new(Utc(2026, 2, 19, 0, 11, 0), "devenv.exe", "Active", 300)
        ];

        IReadOnlyList<StateStackRowLayout> result = builder.BuildDailyStateStackRows(rows, window, trackWidth: 960);
        StateStackRowLayout running = Assert.Single(result);
        Assert.Equal("Running", running.Label);
        AssertApproximately(running.Columns.Sum(x => x.Width), 960);
        Assert.Contains(running.Columns, x => x.Entries.Count == 2);
        Assert.Contains(running.Columns, x => x.Entries.Count == 1);
    }

    [Fact]
    public void BuildDailyStateStackRows_NormalizesSameAppAcrossStatesWithinSameBucket()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 20, 0, 0, 0),
            TimeSpan.FromMinutes(1));

        IReadOnlyList<TimelineUsageRow> rows =
        [
            new(Utc(2026, 2, 19, 0, 10, 0), "devenv.exe", "Active", 150),
            new(Utc(2026, 2, 19, 0, 10, 0), "devenv.exe", "Open", 150)
        ];

        IReadOnlyList<StateStackRowLayout> result = builder.BuildDailyStateStackRows(rows, window, trackWidth: 960);
        StateStackRowLayout running = Assert.Single(result);
        StackedColumnLayout column = Assert.Single(running.Columns, x => !x.IsNoData);
        StackedEntryLayout entry = Assert.Single(column.Entries);
        Assert.Contains("devenv.exe", entry.Tooltip);
    }

    [Fact]
    public void BuildDailyStateStackRows_OrdersConcurrentAppsByName()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 20, 0, 0, 0),
            TimeSpan.FromMinutes(1));

        IReadOnlyList<TimelineUsageRow> rows =
        [
            new(Utc(2026, 2, 19, 0, 10, 0), "powershell.exe", "Active", 300),
            new(Utc(2026, 2, 19, 0, 10, 0), "devenv.exe", "Active", 300),
            new(Utc(2026, 2, 19, 0, 10, 0), "msedge.exe", "Active", 300)
        ];

        IReadOnlyList<StateStackRowLayout> result = builder.BuildDailyStateStackRows(rows, window, trackWidth: 960);
        StateStackRowLayout running = Assert.Single(result);
        StackedColumnLayout column = Assert.Single(running.Columns, x => !x.IsNoData);

        Assert.Equal(["devenv.exe", "msedge.exe", "powershell.exe"], column.Entries.Select(x => x.Label).ToArray());
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
    public void BuildWeeklyStateStackRows_ReturnsSevenDateRows_WithFullTrackWidth()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = CreateWeekWindow(Utc(2026, 2, 13, 0, 0, 0));
        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 13, 8, "devenv.exe", "Active", 3600),
            Row(2026, 2, 13, 8, "powershell.exe", "Open", 1800),
            Row(2026, 2, 14, 10, "msedge.exe", "Open", 3600)
        ];

        IReadOnlyList<StateStackRowLayout> result = builder.BuildWeeklyStateStackRows(rows, window, trackWidth: 960);

        Assert.Equal(7, result.Count);
        foreach (StateStackRowLayout row in result)
        {
            Assert.Matches(@"\d{2}/\d{2} \(.+\)", row.Label);
            AssertApproximately(row.Columns.Sum(x => x.Width), 960);
            Assert.True(ParseDuration(row.TotalLabel) <= TimeSpan.FromHours(24));
        }
    }

    [Fact]
    public void BuildAppTimelineRows_UsesAppToneColors_AndKeepsTrackWidth()
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
            TimelineLayoutBuilder.ColorForAppState("devenv.exe", "Active"),
            TimelineLayoutBuilder.ColorForAppState("devenv.exe", "Open"),
            TimelineLayoutBuilder.ColorForAppState("devenv.exe", "Minimized")
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
    public void BuildAppTimelineRows_FillsHourBucketWhenAppHasData()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = CreateWeekWindow(Utc(2026, 2, 13, 0, 0, 0));
        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 13, 8, "devenv.exe", "Active", 600),
            Row(2026, 2, 13, 8, "devenv.exe", "Open", 300)
        ];

        IReadOnlyList<TimelineRowLayout> result = builder.BuildAppTimelineRows(rows, window, "devenv.exe", trackWidth: 960);
        TimelineRowLayout day = result[0];
        double hourWidth = 960.0 / 24.0;
        double coloredWidth = day.Segments.Where(x => !x.IsNoData).Sum(x => x.Width);

        AssertApproximately(coloredWidth, hourWidth);
    }

    [Fact]
    public void BuildDailyAppRows_ReturnsRowsPerApp_WithAppToneColors()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = Create24HourWindow(Utc(2026, 2, 19, 0, 0, 0));
        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 19, 8, "devenv.exe", "Active", 2400),
            Row(2026, 2, 19, 8, "devenv.exe", "Open", 1200),
            Row(2026, 2, 19, 9, "powershell.exe", "Open", 1800),
            Row(2026, 2, 19, 10, "powershell.exe", "Minimized", 600)
        ];

        IReadOnlyList<StateLaneLayout> result = builder.BuildDailyAppRows(rows, window, trackWidth: 960);

        Assert.Equal(2, result.Count);
        foreach (StateLaneLayout row in result)
        {
            HashSet<string> expectedColors =
            [
                TimelineLayoutBuilder.ColorForAppState(row.Label, "Active"),
                TimelineLayoutBuilder.ColorForAppState(row.Label, "Open"),
                TimelineLayoutBuilder.ColorForAppState(row.Label, "Minimized")
            ];

            AssertApproximately(row.Segments.Sum(x => x.Width), 960);
            foreach (SegmentLayout segment in row.Segments.Where(x => !x.IsNoData))
            {
                Assert.Contains(segment.ColorHex, expectedColors);
            }
        }
    }

    [Fact]
    public void BuildDailyAppRows_WithMinuteBuckets_DoesNotCollapseIntoHourlyIndex()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 20, 0, 0, 0),
            TimeSpan.FromMinutes(1));

        IReadOnlyList<TimelineUsageRow> rows =
        [
            new(Utc(2026, 2, 19, 0, 1, 0), "devenv.exe", "Active", 300),
            new(Utc(2026, 2, 19, 0, 59, 0), "devenv.exe", "Active", 300)
        ];

        IReadOnlyList<StateLaneLayout> result = builder.BuildDailyAppRows(rows, window, trackWidth: 960);
        StateLaneLayout lane = Assert.Single(result);
        int nonNoDataCount = lane.Segments.Count(x => !x.IsNoData);

        Assert.Equal(2, nonNoDataCount);
        AssertApproximately(lane.Segments.Sum(x => x.Width), 960);
    }

    [Fact]
    public void BuildDailyAppRows_WithMinuteBuckets_MergesContiguousSameStateSegments()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 20, 0, 0, 0),
            TimeSpan.FromMinutes(1));

        IReadOnlyList<TimelineUsageRow> rows =
        [
            new(Utc(2026, 2, 19, 0, 1, 0), "devenv.exe", "Active", 120),
            new(Utc(2026, 2, 19, 0, 2, 0), "devenv.exe", "Active", 120),
            new(Utc(2026, 2, 19, 0, 3, 0), "devenv.exe", "Active", 120)
        ];

        IReadOnlyList<StateLaneLayout> result = builder.BuildDailyAppRows(rows, window, trackWidth: 960);
        StateLaneLayout lane = Assert.Single(result);
        int nonNoDataCount = lane.Segments.Count(x => !x.IsNoData);

        Assert.Equal(1, nonNoDataCount);
        AssertApproximately(lane.Segments.Sum(x => x.Width), 960);
    }

    [Fact]
    public void BuildAppDailyLanes_UsesAppToneColorPerState()
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

        foreach (StateLaneLayout lane in lanes)
        {
            string expectedColor = TimelineLayoutBuilder.ColorForAppState(app, lane.Label);
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
