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
    public void BuildDailyStateStackRows_ReturnsSingleActiveRow_AndUsesTopAppPerBucket()
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
        StateStackRowLayout active = Assert.Single(result);
        Assert.Equal("Active", active.Label);
        AssertApproximately(active.Columns.Sum(x => x.Width), 960);
        Assert.Equal(1, active.Columns.Count(x => !x.IsNoData));
        Assert.All(active.Columns.Where(x => !x.IsNoData), x => Assert.Single(x.Entries));
    }

    [Fact]
    public void BuildDailyStateStackRows_IgnoresNonActiveStatesWithinSameBucket()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 20, 0, 0, 0),
            TimeSpan.FromMinutes(1));

        IReadOnlyList<TimelineUsageRow> rows =
        [
            new(Utc(2026, 2, 19, 0, 10, 0), "devenv.exe", "Active", 300),
            new(Utc(2026, 2, 19, 0, 10, 0), "devenv.exe", "Open", 150),
            new(Utc(2026, 2, 19, 0, 10, 0), "powershell.exe", "Open", 300)
        ];

        IReadOnlyList<StateStackRowLayout> result = builder.BuildDailyStateStackRows(rows, window, trackWidth: 960);
        StateStackRowLayout active = Assert.Single(result);
        StackedColumnLayout column = Assert.Single(active.Columns, x => !x.IsNoData);
        StackedEntryLayout entry = Assert.Single(column.Entries);
        Assert.Contains("devenv.exe", entry.Tooltip);
        Assert.DoesNotContain(column.Entries, x => x.Label == "powershell.exe");
        Assert.Equal("00:01", active.TotalLabel);
    }

    [Fact]
    public void BuildDailyStateStackRows_PicksAlphabeticalTopAppWhenSecondsTie()
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
        StateStackRowLayout active = Assert.Single(result);
        StackedColumnLayout column = Assert.Single(active.Columns, x => !x.IsNoData);

        Assert.Single(column.Entries);
        Assert.Equal("devenv.exe", column.Entries[0].Label);
    }

    [Fact]
    public void BuildDailyStateStackRows_UsesTopAppSecondsForDisplayedWidth()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 19, 1, 0, 0),
            TimeSpan.FromMinutes(5));

        // appA/appB both exceed visible threshold (>=300s) in this window.
        IReadOnlyList<TimelineUsageRow> rows =
        [
            // 00:00-00:05
            new(Utc(2026, 2, 19, 0, 0, 0), "appA.exe", "Active", 300),
            // 00:20-00:25 (target bucket): top app is appB(240s), appA has 60s
            new(Utc(2026, 2, 19, 0, 20, 0), "appA.exe", "Active", 60),
            new(Utc(2026, 2, 19, 0, 20, 0), "appB.exe", "Active", 240),
            // 00:40-00:45
            new(Utc(2026, 2, 19, 0, 40, 0), "appB.exe", "Active", 300)
        ];

        IReadOnlyList<StateStackRowLayout> result = builder.BuildDailyStateStackRows(rows, window, trackWidth: 120);
        StateStackRowLayout active = Assert.Single(result);

        StackedColumnLayout target = Assert.Single(
            active.Columns,
            x => !x.IsNoData &&
                 x.Entries.Any(e => string.Equals(e.Label, "appB.exe", StringComparison.OrdinalIgnoreCase)) &&
                 Math.Abs(x.Width - 8) < 0.001);

        // 1h / 5m = 12 buckets => bucketWidth = 10.
        // top app seconds = 240/300 => expected width = 8.
        AssertApproximately(target.Width, 8);
        StackedEntryLayout entry = Assert.Single(target.Entries);
        Assert.Equal("appB.exe", entry.Label);
        Assert.Contains("00:04", entry.Tooltip);
    }

    [Fact]
    public void BuildDailyStateStackRowsFromIntervals_UsesContinuousIntervals()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 19, 1, 0, 0),
            TimeSpan.FromMinutes(5));

        IReadOnlyList<ActiveIntervalRow> intervals =
        [
            new("appA.exe", Utc(2026, 2, 19, 0, 0, 0), Utc(2026, 2, 19, 0, 3, 0)),
            new("appA.exe", Utc(2026, 2, 19, 0, 30, 0), Utc(2026, 2, 19, 0, 33, 0)),
            new("appB.exe", Utc(2026, 2, 19, 0, 20, 0), Utc(2026, 2, 19, 0, 24, 0)),
            new("appB.exe", Utc(2026, 2, 19, 0, 50, 0), Utc(2026, 2, 19, 0, 53, 0))
        ];

        IReadOnlyList<StateStackRowLayout> result = builder.BuildDailyStateStackRowsFromIntervals(intervals, window, trackWidth: 120);
        StateStackRowLayout active = Assert.Single(result);

        // appA/appB are both >=300s and visible. A 4-minute segment should be width 8 on 120px/1h.
        StackedColumnLayout appBSegment = Assert.Single(
            active.Columns,
            x => !x.IsNoData &&
                 Math.Abs(x.Width - 8) < 0.001 &&
                 x.Entries.Any(e => e.Label == "appB.exe"));

        Assert.Single(appBSegment.Entries);
        Assert.Equal("appB.exe", appBSegment.Entries[0].Label);
    }

    [Fact]
    public void BuildDailyStateStackRowsFromIntervals_TooltipIncludesSeconds()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 19, 0, 10, 0),
            TimeSpan.FromMinutes(5));

        IReadOnlyList<ActiveIntervalRow> intervals =
        [
            new("appA.exe", Utc(2026, 2, 19, 0, 0, 0), Utc(2026, 2, 19, 0, 5, 0))
        ];

        IReadOnlyList<StateStackRowLayout> result = builder.BuildDailyStateStackRowsFromIntervals(intervals, window, trackWidth: 100);
        StateStackRowLayout active = Assert.Single(result);
        StackedColumnLayout column = Assert.Single(active.Columns, x => !x.IsNoData);
        StackedEntryLayout entry = Assert.Single(column.Entries);

        Assert.Matches(@".*\d{2}:\d{2}:\d{2}-\d{2}:\d{2}:\d{2} \| \d{2}:\d{2}:\d{2}$", entry.Tooltip);
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
    public void BuildAppTimelineRows_UsesProportionalWidthWithinHourBucket()
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
        double expectedWidth = hourWidth * (900.0 / 3600.0);
        double coloredWidth = day.Segments.Where(x => !x.IsNoData).Sum(x => x.Width);

        AssertApproximately(coloredWidth, expectedWidth);
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
    public void BuildDailyAppRowsFromIntervals_UsesContinuousStateSegments()
    {
        var builder = new TimelineLayoutBuilder(topAppCount: 8);
        UsageQueryWindow window = new(
            Utc(2026, 2, 19, 0, 0, 0),
            Utc(2026, 2, 19, 1, 0, 0),
            TimeSpan.FromMinutes(5));

        IReadOnlyList<AppStateIntervalRow> intervals =
        [
            new("devenv.exe", "Active", Utc(2026, 2, 19, 0, 0, 0), Utc(2026, 2, 19, 0, 3, 0)),
            new("devenv.exe", "Open", Utc(2026, 2, 19, 0, 3, 0), Utc(2026, 2, 19, 0, 8, 0)),
            new("devenv.exe", "Minimized", Utc(2026, 2, 19, 0, 8, 0), Utc(2026, 2, 19, 0, 10, 0)),
            // keep visible threshold >= 300s for devenv.exe
            new("devenv.exe", "Open", Utc(2026, 2, 19, 0, 40, 0), Utc(2026, 2, 19, 0, 45, 0))
        ];

        IReadOnlyList<StateLaneLayout> rows = builder.BuildDailyAppRowsFromIntervals(intervals, window, trackWidth: 120);
        StateLaneLayout row = Assert.Single(rows);

        Assert.Equal("devenv.exe", row.Label);
        AssertApproximately(row.Segments.Sum(x => x.Width), 120);
        Assert.Contains(row.Segments, x => !x.IsNoData && x.Tooltip.Contains("Active", StringComparison.Ordinal));
        Assert.Contains(row.Segments, x => !x.IsNoData && x.Tooltip.Contains("Open", StringComparison.Ordinal));
        Assert.Contains(row.Segments, x => !x.IsNoData && x.Tooltip.Contains("Minimized", StringComparison.Ordinal));
        Assert.Contains(row.Segments, x => !x.IsNoData && x.Tooltip.Contains("00:03:00", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildAppTimelineRowsFromIntervals_ReturnsSevenRows_WithFullTrackWidth()
    {
        var builder = new TimelineLayoutBuilder();
        UsageQueryWindow window = CreateWeekWindow(Utc(2026, 2, 13, 0, 0, 0));
        IReadOnlyList<AppStateIntervalRow> intervals =
        [
            new("devenv.exe", "Active", Utc(2026, 2, 13, 8, 0, 0), Utc(2026, 2, 13, 8, 6, 0)),
            new("devenv.exe", "Open", Utc(2026, 2, 13, 8, 6, 0), Utc(2026, 2, 13, 8, 10, 0)),
            new("devenv.exe", "Active", Utc(2026, 2, 14, 9, 0, 0), Utc(2026, 2, 14, 9, 5, 0))
        ];

        IReadOnlyList<TimelineRowLayout> rows = builder.BuildAppTimelineRowsFromIntervals(intervals, window, "devenv.exe", trackWidth: 760);

        Assert.Equal(7, rows.Count);
        foreach (TimelineRowLayout row in rows)
        {
            AssertApproximately(row.Segments.Sum(x => x.Width), 760);
        }
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
        Assert.Equal(TimelineLayoutBuilder.OtherColorKey, legend[2].ColorHex);
    }

    [Fact]
    public void BuildOverviewLegend_UsesActiveOnlyRows()
    {
        var builder = new TimelineLayoutBuilder(topAppCount: 8);
        IReadOnlyList<TimelineUsageRow> rows =
        [
            Row(2026, 2, 19, 0, "a.exe", "Active", 7200),
            Row(2026, 2, 19, 1, "b.exe", "Open", 7200),
            Row(2026, 2, 19, 2, "c.exe", "Minimized", 7200)
        ];

        IReadOnlyList<LegendItemLayout> legend = builder.BuildOverviewLegend(rows);

        LegendItemLayout item = Assert.Single(legend);
        Assert.Equal("a.exe", item.Label);
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
