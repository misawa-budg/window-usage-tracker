using static WinTracker.Shared.Analytics.TimelinePresentation;

namespace WinTracker.Shared.Analytics;

public sealed class TimelineLayoutBuilder
{
    public const string NoDataTooltip = "記録なし";
    public const string OtherLabel = "Other";
    public const string OtherColorKey = "BrushPastelRed";
    private const double MinVisibleSeconds = 0.0;
    private const string OverviewState = "Active";

    private readonly int _topAppCount;

    public TimelineLayoutBuilder(int topAppCount = 8)
    {
        _topAppCount = Math.Max(1, topAppCount);
    }

    public IReadOnlyList<string> BuildAppNames(IReadOnlyList<AppStateIntervalRow> intervals)
    {
        HashSet<string> visibleApps = BuildVisibleAppSet(intervals, MinVisibleSeconds);
        return intervals
            .Where(x => visibleApps.Contains(x.ExeName))
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                ExeName = g.Key,
                Seconds = g.Sum(v => (v.StateEndUtc - v.StateStartUtc).TotalSeconds)
            })
            .Where(x => x.Seconds > 0)
            .OrderByDescending(x => x.Seconds)
            .ThenBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ExeName)
            .ToList();
    }

    public IReadOnlyList<LegendItemLayout> BuildOverviewLegend(IReadOnlyList<ActiveIntervalRow> activeIntervals)
    {
        HashSet<string> visibleApps = BuildVisibleAppSet(activeIntervals, MinVisibleSeconds);
        var apps = activeIntervals
            .Where(x => visibleApps.Contains(x.ExeName))
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                ExeName = g.Key,
                Seconds = g.Sum(v => (v.StateEndUtc - v.StateStartUtc).TotalSeconds)
            })
            .Where(x => x.Seconds > 0)
            .OrderByDescending(x => x.Seconds)
            .ThenBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var legend = new List<LegendItemLayout>();
        foreach (var app in apps.Take(_topAppCount))
        {
            legend.Add(new LegendItemLayout(app.ExeName, ColorForKey(app.ExeName)));
        }

        if (apps.Count > _topAppCount)
        {
            legend.Add(new LegendItemLayout(OtherLabel, OtherColorKey));
        }

        return legend;
    }

    public IReadOnlyList<LegendItemLayout> BuildAppLegend()
    {
        return
        [
            new LegendItemLayout("最前面（濃）", ColorForAppState("legend", "Active")),
            new LegendItemLayout("通常（中）", ColorForAppState("legend", "Open")),
            new LegendItemLayout("最小化（淡）", ColorForAppState("legend", "Minimized"))
        ];
    }

    public IReadOnlyList<LegendItemLayout> BuildAppRunningLegend()
    {
        return
        [
            new LegendItemLayout("最前面・通常・最小化をまとめた時間", "BrushTextSecondary")
        ];
    }

    public IReadOnlyList<StateLaneLayout> BuildDailyAppRowsFromIntervals(
        IReadOnlyList<AppStateIntervalRow> intervals,
        UsageQueryWindow window,
        double trackWidth)
    {
        double windowSeconds = (window.ToUtc - window.FromUtc).TotalSeconds;
        if (windowSeconds <= 0)
        {
            return [];
        }

        HashSet<string> visibleApps = BuildVisibleAppSet(intervals, MinVisibleSeconds);
        IReadOnlyList<string> appNames = BuildAppNames(intervals)
            .Where(visibleApps.Contains)
            .ToList();

        var rows = new List<StateLaneLayout>(appNames.Count);
        foreach (string appName in appNames)
        {
            List<AppStateIntervalRow> appIntervals = intervals
                .Where(x => string.Equals(x.ExeName, appName, StringComparison.OrdinalIgnoreCase) &&
                            x.StateEndUtc > window.FromUtc &&
                            x.StateStartUtc < window.ToUtc)
                .Select(x =>
                {
                    DateTimeOffset startUtc = x.StateStartUtc < window.FromUtc ? window.FromUtc : x.StateStartUtc;
                    DateTimeOffset endUtc = x.StateEndUtc > window.ToUtc ? window.ToUtc : x.StateEndUtc;
                    return new AppStateIntervalRow(x.ExeName, x.State, startUtc, endUtc);
                })
                .Where(x => x.StateEndUtc > x.StateStartUtc)
                .ToList();

            var accumulator = new SegmentAccumulator();
            double laneTotalSeconds = 0;

            foreach (IntervalSlice<AppStateIntervalRow> slice in IntervalSweep.Build(appIntervals, window.FromUtc, window.ToUtc,
                x => x.StateStartUtc, x => x.StateEndUtc, CompareStateIntervals))
            {
                DateTimeOffset sliceStartUtc = slice.Start;
                DateTimeOffset sliceEndUtc = slice.End;
                double sliceSeconds = (sliceEndUtc - sliceStartUtc).TotalSeconds;
                double sliceWidth = trackWidth * (sliceSeconds / windowSeconds);

                if (slice.Value is null)
                {
                    accumulator.AddNoData(sliceWidth);
                    continue;
                }

                string state = slice.Value.Value.State;
                laneTotalSeconds += sliceSeconds;
                accumulator.AddData(
                    sliceWidth,
                    ColorForAppState(appName, state),
                    $"{appName}|{state}",
                    $"{BrowserServices.DisplayName(appName) ?? appName} | {state}",
                    sliceStartUtc.ToLocalTime(),
                    sliceEndUtc.ToLocalTime(),
                    sliceSeconds);
            }

            rows.Add(new StateLaneLayout(
                appName,
                ToDuration(laneTotalSeconds),
                accumulator.Build()));
        }

        return rows;
    }

    public IReadOnlyList<TimelineRowLayout> BuildAppTimelineRowsFromIntervals(
        IReadOnlyList<AppStateIntervalRow> intervals,
        UsageQueryWindow window,
        string appName,
        double trackWidth)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return [];
        }

        HashSet<string> visibleApps = BuildVisibleAppSet(intervals, MinVisibleSeconds);
        if (!visibleApps.Contains(appName))
        {
            return [];
        }

        var rows = new List<TimelineRowLayout>();
        foreach (DateTimeOffset dayStart in EnumerateDayBuckets(window))
        {
            DateTimeOffset dayEnd = dayStart.AddDays(1);
            if (dayEnd > window.ToUtc)
            {
                dayEnd = window.ToUtc;
            }

            double daySeconds = (dayEnd - dayStart).TotalSeconds;
            if (daySeconds <= 0)
            {
                rows.Add(new TimelineRowLayout(
                    FormatBucketLabel(dayStart, dayEnd),
                    "00:00",
                    [new SegmentLayout(trackWidth, NoDataTooltip, string.Empty, true)]));
                continue;
            }

            List<AppStateIntervalRow> dayIntervals = intervals
                .Where(x => string.Equals(x.ExeName, appName, StringComparison.OrdinalIgnoreCase) &&
                            x.StateEndUtc > dayStart &&
                            x.StateStartUtc < dayEnd)
                .Select(x =>
                {
                    DateTimeOffset startUtc = x.StateStartUtc < dayStart ? dayStart : x.StateStartUtc;
                    DateTimeOffset endUtc = x.StateEndUtc > dayEnd ? dayEnd : x.StateEndUtc;
                    return new AppStateIntervalRow(x.ExeName, x.State, startUtc, endUtc);
                })
                .Where(x => x.StateEndUtc > x.StateStartUtc)
                .ToList();

            var accumulator = new SegmentAccumulator();
            double dayTotalSeconds = 0;

            foreach (IntervalSlice<AppStateIntervalRow> slice in IntervalSweep.Build(dayIntervals, dayStart, dayEnd,
                x => x.StateStartUtc, x => x.StateEndUtc, CompareStateIntervals))
            {
                DateTimeOffset sliceStartUtc = slice.Start;
                DateTimeOffset sliceEndUtc = slice.End;
                double sliceSeconds = (sliceEndUtc - sliceStartUtc).TotalSeconds;
                double sliceWidth = trackWidth * (sliceSeconds / daySeconds);

                if (slice.Value is null)
                {
                    accumulator.AddNoData(sliceWidth);
                    continue;
                }

                string state = slice.Value.Value.State;
                dayTotalSeconds += sliceSeconds;
                accumulator.AddData(
                    sliceWidth,
                    ColorForAppState(appName, state),
                    $"{appName}|{state}",
                    $"{BrowserServices.DisplayName(appName) ?? appName} | {state}",
                    sliceStartUtc.ToLocalTime(),
                    sliceEndUtc.ToLocalTime(),
                    sliceSeconds);
            }

            rows.Add(new TimelineRowLayout(
                FormatBucketLabel(dayStart, dayEnd),
                ToDuration(dayTotalSeconds),
                accumulator.Build()));
        }

        return rows;
    }

    public IReadOnlyList<StateStackRowLayout> BuildDailyStateStackRowsFromIntervals(
        IReadOnlyList<ActiveIntervalRow> activeIntervals,
        UsageQueryWindow window,
        double trackWidth)
    {
        double windowSeconds = (window.ToUtc - window.FromUtc).TotalSeconds;
        if (windowSeconds <= 0)
        {
            return
            [
                new StateStackRowLayout(
                    OverviewState,
                    "00:00",
                    [new StackedColumnLayout(trackWidth, true, [])])
            ];
        }

        List<ActiveIntervalRow> clipped = activeIntervals
            .Where(x => x.StateEndUtc > window.FromUtc && x.StateStartUtc < window.ToUtc)
            .Select(x =>
            {
                DateTimeOffset startUtc = x.StateStartUtc < window.FromUtc ? window.FromUtc : x.StateStartUtc;
                DateTimeOffset endUtc = x.StateEndUtc > window.ToUtc ? window.ToUtc : x.StateEndUtc;
                return new ActiveIntervalRow(x.ExeName, startUtc, endUtc);
            })
            .Where(x => x.StateEndUtc > x.StateStartUtc)
            .ToList();

        HashSet<string> visibleApps = BuildVisibleAppSet(clipped, MinVisibleSeconds);
        List<ActiveIntervalRow> visibleIntervals = clipped
            .Where(x => visibleApps.Contains(x.ExeName))
            .ToList();
        HashSet<string> namedApps = BuildOverviewLegend(activeIntervals)
            .Where(x => x.Label != OtherLabel)
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var accumulator = new StateStackAccumulator(OverviewState);
        double totalSeconds = 0;

        foreach (IntervalSlice<ActiveIntervalRow> slice in IntervalSweep.Build(visibleIntervals, window.FromUtc, window.ToUtc,
            x => x.StateStartUtc, x => x.StateEndUtc, CompareActiveIntervals))
        {
            DateTimeOffset sliceStartUtc = slice.Start;
            DateTimeOffset sliceEndUtc = slice.End;
            double sliceSeconds = (sliceEndUtc - sliceStartUtc).TotalSeconds;
            double sliceWidth = trackWidth * (sliceSeconds / windowSeconds);

            if (slice.Value is null)
            {
                accumulator.AddNoData(sliceWidth);
                continue;
            }

            ActiveIntervalRow topInterval = slice.Value.Value;

            totalSeconds += sliceSeconds;
            accumulator.AddData(
                sliceWidth,
                [new AppUsage(topInterval.ExeName, sliceSeconds,
                    namedApps.Contains(topInterval.ExeName) ? ColorForKey(topInterval.ExeName) : OtherColorKey)],
                sliceStartUtc.ToLocalTime(),
                sliceEndUtc.ToLocalTime());
        }

        return
        [
            new StateStackRowLayout(
                OverviewState,
                ToDuration(totalSeconds),
                accumulator.Build())
        ];
    }

    public IReadOnlyList<StateStackRowLayout> BuildWeeklyStateStackRowsFromIntervals(
        IReadOnlyList<ActiveIntervalRow> activeIntervals,
        UsageQueryWindow window,
        double trackWidth)
    {
        var rows = new List<StateStackRowLayout>();

        foreach (DateTimeOffset dayStart in EnumerateDayBuckets(window))
        {
            DateTimeOffset dayEnd = dayStart.AddDays(1);
            if (dayEnd > window.ToUtc)
            {
                dayEnd = window.ToUtc;
            }

            UsageQueryWindow dayWindow = new(dayStart, dayEnd, window.BucketSize);
            // Use the whole selected period for the legend mapping, then clip inside the daily builder.
            StateStackRowLayout active = BuildDailyStateStackRowsFromIntervals(activeIntervals, dayWindow, trackWidth)[0];
            rows.Add(new StateStackRowLayout(
                $"{dayStart.ToLocalTime():MM/dd (ddd)}",
                active.TotalLabel,
                active.Columns));
        }

        return rows;
    }

    private static HashSet<string> BuildVisibleAppSet(IReadOnlyList<ActiveIntervalRow> activeIntervals, double minVisibleSeconds)
    {
        return activeIntervals
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Sum(v => (v.StateEndUtc - v.StateStartUtc).TotalSeconds) >= minVisibleSeconds)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildVisibleAppSet(IReadOnlyList<AppStateIntervalRow> intervals, double minVisibleSeconds)
    {
        return intervals
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Sum(v => (v.StateEndUtc - v.StateStartUtc).TotalSeconds) >= minVisibleSeconds)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static int CompareStateIntervals(AppStateIntervalRow left, AppStateIntervalRow right)
    {
        int order = AppStatePriority.Get(right.State).CompareTo(AppStatePriority.Get(left.State));
        if (order == 0) order = right.StateStartUtc.CompareTo(left.StateStartUtc);
        return order == 0 ? StringComparer.OrdinalIgnoreCase.Compare(left.State, right.State) : order;
    }

    private static int CompareActiveIntervals(ActiveIntervalRow left, ActiveIntervalRow right)
    {
        int order = right.StateStartUtc.CompareTo(left.StateStartUtc);
        return order == 0 ? StringComparer.OrdinalIgnoreCase.Compare(left.ExeName, right.ExeName) : order;
    }

    private sealed class SegmentAccumulator
    {
        private readonly List<PendingSegment> _segments = [];

        public void AddNoData(double width)
        {
            if (width <= 0)
            {
                return;
            }

            if (_segments.Count > 0 && _segments[^1].IsNoData)
            {
                PendingSegment merged = _segments[^1];
                merged.Width += width;
                _segments[^1] = merged;
                return;
            }

            _segments.Add(PendingSegment.CreateNoData(width));
        }

        public void AddData(
            double width,
            string colorHex,
            string mergeKey,
            string tooltipPrefix,
            DateTimeOffset localStart,
            DateTimeOffset localEnd,
            double seconds)
        {
            if (width <= 0)
            {
                return;
            }

            if (_segments.Count > 0)
            {
                PendingSegment last = _segments[^1];
                if (!last.IsNoData &&
                    string.Equals(last.MergeKey, mergeKey, StringComparison.Ordinal) &&
                    last.LocalEnd == localStart)
                {
                    last.Width += width;
                    last.LocalEnd = localEnd;
                    last.Seconds += seconds;
                    _segments[^1] = last;
                    return;
                }
            }

            _segments.Add(PendingSegment.CreateData(width, colorHex, mergeKey, tooltipPrefix, localStart, localEnd, seconds));
        }

        public IReadOnlyList<SegmentLayout> Build()
        {
            var result = new List<SegmentLayout>(_segments.Count);
            foreach (PendingSegment segment in _segments)
            {
                if (segment.IsNoData)
                {
                    result.Add(new SegmentLayout(
                        segment.Width,
                        NoDataTooltip,
                        string.Empty,
                        IsNoData: true));
                    continue;
                }

                string tooltip = $"{segment.TooltipPrefix} | {FormatTooltipTimeRange(segment.LocalStart, segment.LocalEnd)} | {ToDurationWithSeconds(segment.Seconds)}";
                result.Add(new SegmentLayout(
                    segment.Width,
                    tooltip,
                    segment.ColorHex));
            }

            return result;
        }

        private struct PendingSegment
        {
            public bool IsNoData { get; init; }
            public double Width { get; set; }
            public string ColorHex { get; init; }
            public string MergeKey { get; init; }
            public string TooltipPrefix { get; init; }
            public DateTimeOffset LocalStart { get; init; }
            public DateTimeOffset LocalEnd { get; set; }
            public double Seconds { get; set; }

            public static PendingSegment CreateNoData(double width) =>
                new()
                {
                    IsNoData = true,
                    Width = width,
                    ColorHex = string.Empty,
                    MergeKey = string.Empty,
                    TooltipPrefix = string.Empty
                };

            public static PendingSegment CreateData(
                double width,
                string colorHex,
                string mergeKey,
                string tooltipPrefix,
                DateTimeOffset localStart,
                DateTimeOffset localEnd,
                double seconds) =>
                new()
                {
                    IsNoData = false,
                    Width = width,
                    ColorHex = colorHex,
                    MergeKey = mergeKey,
                    TooltipPrefix = tooltipPrefix,
                    LocalStart = localStart,
                    LocalEnd = localEnd,
                    Seconds = seconds
                };
        }
    }

    private readonly record struct AppUsage(string ExeName, double Seconds, string ColorHex);

    private sealed class StateStackAccumulator
    {
        private readonly string _state;
        private readonly List<PendingStackColumn> _columns = [];

        public StateStackAccumulator(string state)
        {
            _state = state;
        }

        public void AddNoData(double width)
        {
            if (width <= 0)
            {
                return;
            }

            if (_columns.Count > 0 && _columns[^1].IsNoData)
            {
                PendingStackColumn merged = _columns[^1];
                merged.Width += width;
                _columns[^1] = merged;
                return;
            }

            _columns.Add(PendingStackColumn.CreateNoData(width));
        }

        public void AddData(
            double width,
            IReadOnlyList<AppUsage> apps,
            DateTimeOffset localStart,
            DateTimeOffset localEnd)
        {
            if (width <= 0 || apps.Count == 0)
            {
                return;
            }

            string key = string.Join('\u001f', apps.Select(x => x.ExeName));
            if (_columns.Count > 0)
            {
                PendingStackColumn last = _columns[^1];
                if (!last.IsNoData &&
                    string.Equals(last.Key, key, StringComparison.Ordinal) &&
                    last.LocalEnd == localStart)
                {
                    last.Width += width;
                    last.LocalEnd = localEnd;
                    foreach (AppUsage app in apps)
                    {
                        last.SecondsByApp[app.ExeName] = last.SecondsByApp.GetValueOrDefault(app.ExeName, 0) + app.Seconds;
                    }

                    _columns[^1] = last;
                    return;
                }
            }

            var secondsByApp = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var colorByApp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var appOrder = new List<string>(apps.Count);
            foreach (AppUsage app in apps)
            {
                appOrder.Add(app.ExeName);
                secondsByApp[app.ExeName] = app.Seconds;
                colorByApp[app.ExeName] = app.ColorHex;
            }

            _columns.Add(PendingStackColumn.CreateData(
                width,
                key,
                appOrder,
                secondsByApp,
                colorByApp,
                localStart,
                localEnd));
        }

        public IReadOnlyList<StackedColumnLayout> Build()
        {
            var result = new List<StackedColumnLayout>(_columns.Count);
            foreach (PendingStackColumn column in _columns)
            {
                if (column.IsNoData)
                {
                    result.Add(new StackedColumnLayout(
                        column.Width,
                        true,
                        []));
                    continue;
                }

                var entries = new List<StackedEntryLayout>(column.AppOrder.Count);
                foreach (string app in column.AppOrder)
                {
                    double seconds = column.SecondsByApp.GetValueOrDefault(app, 0);
                    string tooltip = $"{_state} | {BrowserServices.DisplayName(app) ?? app} | {FormatTooltipTimeRange(column.LocalStart, column.LocalEnd)} | {ToDurationWithSeconds(seconds)}";
                    entries.Add(new StackedEntryLayout(
                        app,
                        column.ColorByApp.GetValueOrDefault(app, OtherColorKey),
                        tooltip));
                }

                result.Add(new StackedColumnLayout(
                    column.Width,
                    false,
                    entries));
            }

            return result;
        }

        private struct PendingStackColumn
        {
            public bool IsNoData { get; set; }
            public double Width { get; set; }
            public string Key { get; set; }
            public IReadOnlyList<string> AppOrder { get; set; }
            public Dictionary<string, double> SecondsByApp { get; set; }
            public Dictionary<string, string> ColorByApp { get; set; }
            public DateTimeOffset LocalStart { get; set; }
            public DateTimeOffset LocalEnd { get; set; }

            public static PendingStackColumn CreateNoData(double width) =>
                new()
                {
                    IsNoData = true,
                    Width = width,
                    Key = string.Empty,
                    AppOrder = [],
                    SecondsByApp = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                    ColorByApp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    LocalStart = default,
                    LocalEnd = default
                };

            public static PendingStackColumn CreateData(
                double width,
                string key,
                IReadOnlyList<string> appOrder,
                Dictionary<string, double> secondsByApp,
                Dictionary<string, string> colorByApp,
                DateTimeOffset localStart,
                DateTimeOffset localEnd) =>
                new()
                {
                    IsNoData = false,
                    Width = width,
                    Key = key,
                    AppOrder = appOrder,
                    SecondsByApp = secondsByApp,
                    ColorByApp = colorByApp,
                    LocalStart = localStart,
                    LocalEnd = localEnd
                };
        }
    }

    private static IEnumerable<DateTimeOffset> EnumerateDayBuckets(UsageQueryWindow window)
    {
        DateTimeOffset cursor = window.FromUtc;
        while (cursor < window.ToUtc)
        {
            yield return cursor;
            cursor = cursor.AddDays(1);
        }
    }

}
