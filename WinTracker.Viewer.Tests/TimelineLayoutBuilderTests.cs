using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class TimelineLayoutBuilderTests
{
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

    private static UsageQueryWindow CreateWeekWindow(DateTimeOffset weekStartUtc) =>
        new(weekStartUtc, weekStartUtc.AddDays(7), TimeSpan.FromHours(1));

    private static DateTimeOffset Utc(int y, int m, int d, int hh, int mm, int ss) =>
        new(y, m, d, hh, mm, ss, TimeSpan.Zero);

    private static void AssertApproximately(double actual, double expected)
    {
        const double tolerance = 0.001;
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
}
