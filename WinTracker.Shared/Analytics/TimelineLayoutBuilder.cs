namespace WinTracker.Shared.Analytics;

public sealed class TimelineLayoutBuilder
{
    public const string NoDataTooltip = "No data";
    public const string OtherLabel = "Other";
    public const string OtherColorHex = "#F8B5B5";
    public const string ActiveColorHex = "#BFA8FF";
    public const string OpenColorHex = "#B8E9C7";
    public const string MinimizedColorHex = "#F9E6A6";
    private const double MinVisibleSeconds = 300.0;

    private static readonly string[] AppStates = ["Active", "Open", "Minimized"];

    private readonly int _topAppCount;

    public TimelineLayoutBuilder(int topAppCount = 8)
    {
        _topAppCount = Math.Max(1, topAppCount);
    }

    public IReadOnlyList<string> BuildAppNames(IReadOnlyList<TimelineUsageRow> timelineRows)
    {
        HashSet<string> visibleApps = BuildVisibleAppSet(timelineRows, MinVisibleSeconds);
        return timelineRows
            .Where(r => visibleApps.Contains(r.ExeName))
            .GroupBy(r => r.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { ExeName = g.Key, Seconds = g.Sum(x => x.Seconds) })
            .Where(x => x.Seconds > 0)
            .OrderByDescending(x => x.Seconds)
            .ThenBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ExeName)
            .ToList();
    }

    public IReadOnlyList<LegendItemLayout> BuildOverviewLegend(IReadOnlyList<TimelineUsageRow> timelineRows)
    {
        HashSet<string> visibleApps = BuildVisibleAppSet(timelineRows, MinVisibleSeconds);
        var apps = timelineRows
            .Where(x => visibleApps.Contains(x.ExeName))
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { ExeName = g.Key, Seconds = g.Sum(x => x.Seconds) })
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
            legend.Add(new LegendItemLayout(OtherLabel, OtherColorHex));
        }

        return legend;
    }

    public IReadOnlyList<LegendItemLayout> BuildAppLegend()
    {
        return
        [
            new LegendItemLayout("Active", ActiveColorHex),
            new LegendItemLayout("Open", OpenColorHex),
            new LegendItemLayout("Minimized", MinimizedColorHex)
        ];
    }

    public IReadOnlyList<StateLaneLayout> BuildDailyOverviewLanes(
        IReadOnlyList<TimelineUsageRow> timelineRows,
        UsageQueryWindow window,
        double trackWidth)
    {
        var lanes = new List<StateLaneLayout>();
        double hourWidth = trackWidth / 24.0;
        DateTimeOffset fromUtc = window.FromUtc;

        foreach (string state in AppStates)
        {
            var segments = new List<SegmentLayout>();
            double laneTotalSeconds = 0;

            var byHour = timelineRows
                .Where(x => string.Equals(x.State, state, StringComparison.OrdinalIgnoreCase))
                .Select(x => new
                {
                    HourIndex = (int)Math.Floor((x.BucketStartUtc - fromUtc).TotalHours),
                    x.ExeName,
                    x.Seconds
                })
                .Where(x => x.HourIndex >= 0 && x.HourIndex < 24)
                .GroupBy(x => x.HourIndex)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(v => v.ExeName, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(v => v.Key, v => v.Sum(t => t.Seconds), StringComparer.OrdinalIgnoreCase));

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset bucketStart = window.FromUtc.AddHours(hour);
                DateTimeOffset bucketEnd = bucketStart.AddHours(1);

                if (!byHour.TryGetValue(hour, out Dictionary<string, double>? byApp))
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                var normalizedByApp = byApp
                    .ToDictionary(
                        x => x.Key,
                        x => Math.Min((double)window.BucketSeconds, x.Value),
                        StringComparer.OrdinalIgnoreCase);

                double rawHourSeconds = normalizedByApp.Values.Sum();
                double occupiedSeconds = Math.Min((double)window.BucketSeconds, rawHourSeconds);
                laneTotalSeconds += occupiedSeconds;

                if (occupiedSeconds <= 0)
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                double scale = rawHourSeconds > 0 ? occupiedSeconds / rawHourSeconds : 0;
                double occupiedWidth = 0;

                foreach ((string exeName, double seconds) in normalizedByApp
                             .OrderByDescending(x => x.Value)
                             .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (seconds <= 0)
                    {
                        continue;
                    }

                    double width = hourWidth * (seconds / window.BucketSeconds) * scale;
                    if (width <= 0)
                    {
                        continue;
                    }

                    DateTimeOffset localStart = bucketStart.ToLocalTime();
                    DateTimeOffset localEnd = bucketEnd.ToLocalTime();
                    segments.Add(new SegmentLayout(
                        width,
                        $"{state} | {exeName} | {localStart:HH\\:mm}-{localEnd:HH\\:mm} | {ToDuration(seconds)}",
                        ColorForKey(exeName)));
                    occupiedWidth += width;
                }

                double gapWidth = Math.Max(0, hourWidth - occupiedWidth);
                if (gapWidth > 0)
                {
                    AddNoDataSegment(segments, gapWidth);
                }
            }

            lanes.Add(new StateLaneLayout(
                state,
                ToDuration(laneTotalSeconds),
                segments));
        }

        return lanes;
    }

    public IReadOnlyList<StateGroupLayout> BuildDailyStateGroups(
        IReadOnlyList<TimelineUsageRow> timelineRows,
        UsageQueryWindow window,
        double trackWidth)
    {
        int bucketCount = CalculateBucketCount(window);
        double bucketWidth = trackWidth / bucketCount;
        DateTimeOffset fromUtc = window.FromUtc;
        var groups = new List<StateGroupLayout>();

        foreach (string state in AppStates)
        {
            Dictionary<string, Dictionary<int, double>> secondsByAppByBucket = timelineRows
                .Where(x => string.Equals(x.State, state, StringComparison.OrdinalIgnoreCase))
                .Select(x => new
                {
                    x.ExeName,
                    BucketIndex = ToBucketIndex(x.BucketStartUtc, fromUtc, window.BucketSeconds),
                    x.Seconds
                })
                .Where(x => x.BucketIndex >= 0 && x.BucketIndex < bucketCount)
                .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(v => v.BucketIndex).ToDictionary(v => v.Key, v => v.Sum(t => t.Seconds)),
                    StringComparer.OrdinalIgnoreCase);

            Dictionary<string, double> totalsByApp = secondsByAppByBucket
                .ToDictionary(
                    x => x.Key,
                    x => x.Value.Sum(v => Math.Min((double)window.BucketSeconds, v.Value)),
                    StringComparer.OrdinalIgnoreCase);

            List<string> topApps = totalsByApp
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Take(_topAppCount)
                .Select(x => x.Key)
                .ToList();

            var lanes = new List<StateLaneLayout>();
            double groupTotalSeconds = 0;

            foreach (string app in topApps)
            {
                Dictionary<int, double> byBucket = secondsByAppByBucket[app];
                var accumulator = new SegmentAccumulator();
                double laneTotalSeconds = 0;

                for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
                {
                    DateTimeOffset bucketStart = window.FromUtc.AddSeconds((long)bucketIndex * window.BucketSeconds);
                    DateTimeOffset bucketEnd = bucketStart.AddSeconds(window.BucketSeconds);

                    if (!byBucket.TryGetValue(bucketIndex, out double seconds))
                    {
                        accumulator.AddNoData(bucketWidth);
                        continue;
                    }

                    double occupiedSeconds = Math.Min((double)window.BucketSeconds, seconds);
                    laneTotalSeconds += occupiedSeconds;
                    if (occupiedSeconds <= 0)
                    {
                        accumulator.AddNoData(bucketWidth);
                        continue;
                    }

                    double width = bucketWidth * (occupiedSeconds / window.BucketSeconds);
                    width = Math.Min(bucketWidth, width);
                    if (width <= 0)
                    {
                        accumulator.AddNoData(bucketWidth);
                        continue;
                    }

                    DateTimeOffset localStart = bucketStart.ToLocalTime();
                    DateTimeOffset localEnd = bucketEnd.ToLocalTime();
                    accumulator.AddData(
                        width,
                        ColorForKey(app),
                        $"{state}|{app}",
                        $"{state} | {app}",
                        localStart,
                        localEnd,
                        occupiedSeconds);

                    double gapWidth = Math.Max(0, bucketWidth - width);
                    if (gapWidth > 0)
                    {
                        accumulator.AddNoData(gapWidth);
                    }
                }

                groupTotalSeconds += laneTotalSeconds;
                lanes.Add(new StateLaneLayout(
                    app,
                    ToDuration(laneTotalSeconds),
                    accumulator.Build()));
            }

            if (totalsByApp.Count > topApps.Count)
            {
                var otherAccumulator = new SegmentAccumulator();
                double otherTotalSeconds = 0;
                HashSet<string> topAppsSet = topApps.ToHashSet(StringComparer.OrdinalIgnoreCase);

                for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
                {
                    DateTimeOffset bucketStart = window.FromUtc.AddSeconds((long)bucketIndex * window.BucketSeconds);
                    DateTimeOffset bucketEnd = bucketStart.AddSeconds(window.BucketSeconds);
                    double otherSeconds = totalsByApp
                        .Where(x => !topAppsSet.Contains(x.Key))
                        .Select(x => secondsByAppByBucket[x.Key].GetValueOrDefault(bucketIndex, 0))
                        .Sum();

                    double occupiedSeconds = Math.Min((double)window.BucketSeconds, otherSeconds);
                    otherTotalSeconds += occupiedSeconds;
                    if (occupiedSeconds <= 0)
                    {
                        otherAccumulator.AddNoData(bucketWidth);
                        continue;
                    }

                    double width = bucketWidth * (occupiedSeconds / window.BucketSeconds);
                    width = Math.Min(bucketWidth, width);
                    if (width <= 0)
                    {
                        otherAccumulator.AddNoData(bucketWidth);
                        continue;
                    }

                    DateTimeOffset localStart = bucketStart.ToLocalTime();
                    DateTimeOffset localEnd = bucketEnd.ToLocalTime();
                    otherAccumulator.AddData(
                        width,
                        OtherColorHex,
                        $"{state}|{OtherLabel}",
                        $"{state} | {OtherLabel}",
                        localStart,
                        localEnd,
                        occupiedSeconds);

                    double gapWidth = Math.Max(0, bucketWidth - width);
                    if (gapWidth > 0)
                    {
                        otherAccumulator.AddNoData(gapWidth);
                    }
                }

                groupTotalSeconds += otherTotalSeconds;
                lanes.Add(new StateLaneLayout(
                    OtherLabel,
                    ToDuration(otherTotalSeconds),
                    otherAccumulator.Build()));
            }

            groups.Add(new StateGroupLayout(
                state,
                ToDuration(groupTotalSeconds),
                lanes));
        }

        return groups;
    }

    public IReadOnlyList<StateLaneLayout> BuildDailyAppRows(
        IReadOnlyList<TimelineUsageRow> timelineRows,
        UsageQueryWindow window,
        double trackWidth)
    {
        int bucketCount = CalculateBucketCount(window);
        double bucketWidth = trackWidth / bucketCount;
        DateTimeOffset fromUtc = window.FromUtc;

        HashSet<string> visibleApps = BuildVisibleAppSet(timelineRows, MinVisibleSeconds);
        IReadOnlyList<string> appNames = BuildAppNames(timelineRows)
            .Where(x => visibleApps.Contains(x))
            .ToList();
        var rows = new List<StateLaneLayout>(appNames.Count);

        foreach (string appName in appNames)
        {
            var accumulator = new SegmentAccumulator();
            double laneTotalSeconds = 0;

            Dictionary<int, Dictionary<string, double>> byBucket = timelineRows
                .Where(x => string.Equals(x.ExeName, appName, StringComparison.OrdinalIgnoreCase))
                .Select(x => new
                {
                    x.State,
                    BucketIndex = ToBucketIndex(x.BucketStartUtc, fromUtc, window.BucketSeconds),
                    x.Seconds
                })
                .Where(x => x.BucketIndex >= 0 && x.BucketIndex < bucketCount)
                .GroupBy(x => x.BucketIndex)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(v => v.State, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(v => v.Key, v => v.Sum(t => t.Seconds), StringComparer.OrdinalIgnoreCase));

            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                DateTimeOffset bucketStart = window.FromUtc.AddSeconds((long)bucketIndex * window.BucketSeconds);
                DateTimeOffset bucketEnd = bucketStart.AddSeconds(window.BucketSeconds);

                if (!byBucket.TryGetValue(bucketIndex, out Dictionary<string, double>? byState))
                {
                    accumulator.AddNoData(bucketWidth);
                    continue;
                }

                double activeSeconds = Math.Min((double)window.BucketSeconds, byState.GetValueOrDefault("Active", 0));
                double openSeconds = Math.Min((double)window.BucketSeconds, byState.GetValueOrDefault("Open", 0));
                double minimizedSeconds = Math.Min((double)window.BucketSeconds, byState.GetValueOrDefault("Minimized", 0));
                double rawHourSeconds = activeSeconds + openSeconds + minimizedSeconds;
                double occupiedSeconds = Math.Min((double)window.BucketSeconds, rawHourSeconds);
                laneTotalSeconds += occupiedSeconds;

                if (occupiedSeconds <= 0)
                {
                    accumulator.AddNoData(bucketWidth);
                    continue;
                }

                double scale = rawHourSeconds > 0 ? occupiedSeconds / rawHourSeconds : 0;
                double occupiedWidth = 0;
                DateTimeOffset localStart = bucketStart.ToLocalTime();
                DateTimeOffset localEnd = bucketEnd.ToLocalTime();

                occupiedWidth += AddDailyStateSegmentForApp(
                    accumulator,
                    appName,
                    "Active",
                    activeSeconds,
                    window.BucketSeconds,
                    ActiveColorHex,
                    bucketWidth,
                    scale,
                    localStart,
                    localEnd);
                occupiedWidth += AddDailyStateSegmentForApp(
                    accumulator,
                    appName,
                    "Open",
                    openSeconds,
                    window.BucketSeconds,
                    OpenColorHex,
                    bucketWidth,
                    scale,
                    localStart,
                    localEnd);
                occupiedWidth += AddDailyStateSegmentForApp(
                    accumulator,
                    appName,
                    "Minimized",
                    minimizedSeconds,
                    window.BucketSeconds,
                    MinimizedColorHex,
                    bucketWidth,
                    scale,
                    localStart,
                    localEnd);

                double gapWidth = Math.Max(0, bucketWidth - occupiedWidth);
                if (gapWidth > 0)
                {
                    accumulator.AddNoData(gapWidth);
                }
            }

            rows.Add(new StateLaneLayout(
                appName,
                ToDuration(laneTotalSeconds),
                accumulator.Build()));
        }

        return rows;
    }

    public IReadOnlyList<StateStackRowLayout> BuildDailyStateStackRows(
        IReadOnlyList<TimelineUsageRow> timelineRows,
        UsageQueryWindow window,
        double trackWidth)
    {
        int bucketCount = CalculateBucketCount(window);
        double bucketWidth = trackWidth / bucketCount;
        DateTimeOffset fromUtc = window.FromUtc;
        HashSet<string> visibleApps = BuildVisibleAppSet(timelineRows, MinVisibleSeconds);
        var byBucket = timelineRows
            .Where(x => visibleApps.Contains(x.ExeName))
            .Select(x => new
            {
                BucketIndex = ToBucketIndex(x.BucketStartUtc, fromUtc, window.BucketSeconds),
                x.ExeName,
                x.Seconds
            })
            .Where(x => x.BucketIndex >= 0 && x.BucketIndex < bucketCount)
            .GroupBy(x => x.BucketIndex)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(v => v.ExeName, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new AppUsage(
                        v.Key,
                        Math.Min((double)window.BucketSeconds, v.Sum(t => t.Seconds)),
                        ColorForKey(v.Key)))
                    .Where(v => v.Seconds > 0)
                    .OrderByDescending(v => v.Seconds)
                    .ThenBy(v => v.ExeName, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var accumulator = new StateStackAccumulator("Running");
        double totalSeconds = 0;

        for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
        {
            DateTimeOffset bucketStartUtc = window.FromUtc.AddSeconds((long)bucketIndex * window.BucketSeconds);
            DateTimeOffset bucketEndUtc = bucketStartUtc.AddSeconds(window.BucketSeconds);

            if (!byBucket.TryGetValue(bucketIndex, out List<AppUsage>? apps) || apps.Count == 0)
            {
                accumulator.AddNoData(bucketWidth);
                continue;
            }

            double occupiedSeconds = Math.Min((double)window.BucketSeconds, apps.Sum(x => x.Seconds));
            totalSeconds += occupiedSeconds;
            if (occupiedSeconds <= 0)
            {
                accumulator.AddNoData(bucketWidth);
                continue;
            }

            accumulator.AddData(
                bucketWidth,
                apps,
                bucketStartUtc.ToLocalTime(),
                bucketEndUtc.ToLocalTime());
        }

        return
        [
            new StateStackRowLayout(
                "Running",
                ToDuration(totalSeconds),
                accumulator.Build())
        ];
    }

    public IReadOnlyList<TimelineRowLayout> BuildOverviewRows(
        IReadOnlyList<TimelineUsageRow> timelineRows,
        UsageQueryWindow window,
        double trackWidth)
    {
        HashSet<string> visibleApps = BuildVisibleAppSet(timelineRows, MinVisibleSeconds);
        List<TimelineUsageRow> filteredRows = timelineRows
            .Where(x => visibleApps.Contains(x.ExeName))
            .ToList();
        var rows = new List<TimelineRowLayout>();
        var totalByApp = filteredRows
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Seconds), StringComparer.OrdinalIgnoreCase);

        HashSet<string> topApps = totalByApp
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(_topAppCount)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (DateTimeOffset dayStart in EnumerateDayBuckets(window))
        {
            DateTimeOffset dayEnd = dayStart.AddDays(1);
            var segments = new List<SegmentLayout>();
            double dayTotalSeconds = 0;
            double hourWidth = trackWidth / 24.0;

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset hourStart = dayStart.AddHours(hour);
                List<TimelineUsageRow> hourRows = filteredRows
                    .Where(x => x.BucketStartUtc == hourStart)
                    .ToList();

                if (hourRows.Count == 0)
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                var byApp = hourRows
                    .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => Math.Min((double)window.BucketSeconds, g.Sum(v => v.Seconds)),
                        StringComparer.OrdinalIgnoreCase);

                double otherSeconds = byApp
                    .Where(x => !topApps.Contains(x.Key))
                    .Sum(x => x.Value);

                var displayByApp = byApp
                    .Where(x => topApps.Contains(x.Key))
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

                if (otherSeconds > 0)
                {
                    displayByApp[OtherLabel] = Math.Min((double)window.BucketSeconds, otherSeconds);
                }

                double rawHourSeconds = displayByApp.Values.Sum();
                double occupiedSeconds = Math.Min((double)window.BucketSeconds, rawHourSeconds);
                dayTotalSeconds += occupiedSeconds;

                if (occupiedSeconds <= 0)
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                double scale = rawHourSeconds > 0 ? occupiedSeconds / rawHourSeconds : 0;
                double occupiedWidth = 0;

                foreach ((string app, double seconds) in displayByApp
                             .OrderByDescending(x => x.Value)
                             .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    double width = hourWidth * (seconds / window.BucketSeconds) * scale;
                    if (width <= 0)
                    {
                        continue;
                    }

                    string color = string.Equals(app, OtherLabel, StringComparison.OrdinalIgnoreCase)
                        ? OtherColorHex
                        : ColorForKey(app);

                    segments.Add(new SegmentLayout(
                        width,
                        $"{app} {ToDuration(seconds)} ({seconds:F0}s)",
                        color));
                    occupiedWidth += width;
                }

                double gapWidth = Math.Max(0, hourWidth - occupiedWidth);
                if (gapWidth > 0)
                {
                    AddNoDataSegment(segments, gapWidth);
                }
            }

            rows.Add(new TimelineRowLayout(
                FormatBucketLabel(dayStart, dayEnd),
                ToDuration(dayTotalSeconds),
                segments));
        }

        return rows;
    }

    public IReadOnlyList<TimelineRowLayout> BuildAppTimelineRows(
        IReadOnlyList<TimelineUsageRow> timelineRows,
        UsageQueryWindow window,
        string appName,
        double trackWidth)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return [];
        }
        HashSet<string> visibleApps = BuildVisibleAppSet(timelineRows, MinVisibleSeconds);
        if (!visibleApps.Contains(appName))
        {
            return [];
        }

        var rows = new List<TimelineRowLayout>();

        foreach (DateTimeOffset dayStart in EnumerateDayBuckets(window))
        {
            DateTimeOffset dayEnd = dayStart.AddDays(1);
            var segments = new List<SegmentLayout>();
            double dayTotalSeconds = 0;
            double hourWidth = trackWidth / 24.0;

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset hourStart = dayStart.AddHours(hour);
                List<TimelineUsageRow> hourRows = timelineRows
                    .Where(x =>
                        x.BucketStartUtc == hourStart &&
                        string.Equals(x.ExeName, appName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (hourRows.Count == 0)
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                var byState = hourRows
                    .GroupBy(x => x.State, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => Math.Min((double)window.BucketSeconds, g.Sum(v => v.Seconds)),
                        StringComparer.OrdinalIgnoreCase);

                double activeSeconds = byState.GetValueOrDefault("Active", 0);
                double openSeconds = byState.GetValueOrDefault("Open", 0);
                double minimizedSeconds = byState.GetValueOrDefault("Minimized", 0);
                double rawHourSeconds = activeSeconds + openSeconds + minimizedSeconds;
                double occupiedSeconds = Math.Min((double)window.BucketSeconds, rawHourSeconds);
                dayTotalSeconds += occupiedSeconds;

                if (occupiedSeconds <= 0)
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                double scale = rawHourSeconds > 0 ? occupiedSeconds / rawHourSeconds : 0;
                double occupiedWidth = 0;

                occupiedWidth += AddStateSegment(
                    segments,
                    "Active",
                    activeSeconds,
                    window.BucketSeconds,
                    ActiveColorHex,
                    hourWidth,
                    scale);
                occupiedWidth += AddStateSegment(
                    segments,
                    "Open",
                    openSeconds,
                    window.BucketSeconds,
                    OpenColorHex,
                    hourWidth,
                    scale);
                occupiedWidth += AddStateSegment(
                    segments,
                    "Minimized",
                    minimizedSeconds,
                    window.BucketSeconds,
                    MinimizedColorHex,
                    hourWidth,
                    scale);

                double gapWidth = Math.Max(0, hourWidth - occupiedWidth);
                if (gapWidth > 0)
                {
                    AddNoDataSegment(segments, gapWidth);
                }
            }

            rows.Add(new TimelineRowLayout(
                FormatBucketLabel(dayStart, dayEnd),
                ToDuration(dayTotalSeconds),
                segments));
        }

        return rows;
    }

    public IReadOnlyList<StateLaneLayout> BuildAppDailyLanes(
        IReadOnlyList<TimelineUsageRow> timelineRows,
        UsageQueryWindow window,
        string appName,
        double trackWidth)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return [];
        }
        HashSet<string> visibleApps = BuildVisibleAppSet(timelineRows, MinVisibleSeconds);
        if (!visibleApps.Contains(appName))
        {
            return [];
        }

        var lanes = new List<StateLaneLayout>();
        double hourWidth = trackWidth / 24.0;
        DateTimeOffset fromUtc = window.FromUtc;

        foreach (string state in AppStates)
        {
            var segments = new List<SegmentLayout>();
            double laneTotalSeconds = 0;

            var byHour = timelineRows
                .Where(x =>
                    string.Equals(x.ExeName, appName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.State, state, StringComparison.OrdinalIgnoreCase))
                .Select(x => new
                {
                    HourIndex = (int)Math.Floor((x.BucketStartUtc - fromUtc).TotalHours),
                    x.Seconds
                })
                .Where(x => x.HourIndex >= 0 && x.HourIndex < 24)
                .GroupBy(x => x.HourIndex)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Seconds));

            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset bucketStart = window.FromUtc.AddHours(hour);
                DateTimeOffset bucketEnd = bucketStart.AddHours(1);

                if (!byHour.TryGetValue(hour, out double seconds))
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                double occupiedSeconds = Math.Min((double)window.BucketSeconds, seconds);
                laneTotalSeconds += occupiedSeconds;
                if (occupiedSeconds <= 0)
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                double width = hourWidth * (occupiedSeconds / window.BucketSeconds);
                width = Math.Min(hourWidth, width);
                if (width <= 0)
                {
                    AddNoDataSegment(segments, hourWidth);
                    continue;
                }

                DateTimeOffset localStart = bucketStart.ToLocalTime();
                DateTimeOffset localEnd = bucketEnd.ToLocalTime();
                segments.Add(new SegmentLayout(
                    width,
                    $"{state} | {appName} | {localStart:HH\\:mm}-{localEnd:HH\\:mm} | {ToDuration(occupiedSeconds)}",
                    ColorForKey(appName)));

                double gapWidth = Math.Max(0, hourWidth - width);
                if (gapWidth > 0)
                {
                    AddNoDataSegment(segments, gapWidth);
                }
            }

            lanes.Add(new StateLaneLayout(
                state,
                ToDuration(laneTotalSeconds),
                segments));
        }

        return lanes;
    }

    public static string ColorForKey(string key)
    {
        uint hash = ComputeStableHash(key);
        int hue = (int)(hash % 360);
        double saturation = 0.46 + (((hash >> 8) % 15) / 100.0);
        double lightness = 0.56 + (((hash >> 16) % 11) / 100.0);
        (byte r, byte g, byte b) = HslToRgb(hue / 360.0, saturation, lightness);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int CalculateBucketCount(UsageQueryWindow window)
    {
        if (window.BucketSeconds <= 0)
        {
            return 1;
        }

        double totalSeconds = Math.Max(0, (window.ToUtc - window.FromUtc).TotalSeconds);
        return Math.Max(1, (int)Math.Ceiling(totalSeconds / window.BucketSeconds));
    }

    private static int ToBucketIndex(DateTimeOffset bucketStartUtc, DateTimeOffset fromUtc, int bucketSeconds)
    {
        if (bucketSeconds <= 0)
        {
            return 0;
        }

        double elapsedSeconds = (bucketStartUtc - fromUtc).TotalSeconds;
        return (int)Math.Floor(elapsedSeconds / bucketSeconds);
    }

    private static HashSet<string> BuildVisibleAppSet(IReadOnlyList<TimelineUsageRow> timelineRows, double minVisibleSeconds)
    {
        return timelineRows
            .GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Sum(v => v.Seconds) >= minVisibleSeconds)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddNoDataSegment(ICollection<SegmentLayout> segments, double width)
    {
        segments.Add(new SegmentLayout(
            width,
            NoDataTooltip,
            string.Empty,
            IsNoData: true));
    }

    private static double AddStateSegment(
        ICollection<SegmentLayout> segments,
        string state,
        double seconds,
        int bucketSeconds,
        string colorHex,
        double hourWidth,
        double scale)
    {
        if (seconds <= 0 || bucketSeconds <= 0 || hourWidth <= 0 || scale <= 0)
        {
            return 0;
        }

        double width = hourWidth * (seconds / bucketSeconds) * scale;
        if (width <= 0)
        {
            return 0;
        }

        segments.Add(new SegmentLayout(
            width,
            $"{state} {ToDuration(seconds)} ({seconds:F0}s)",
            colorHex));
        return width;
    }

    private static double AddDailyStateSegmentForApp(
        SegmentAccumulator accumulator,
        string appName,
        string state,
        double seconds,
        int bucketSeconds,
        string colorHex,
        double hourWidth,
        double scale,
        DateTimeOffset localStart,
        DateTimeOffset localEnd)
    {
        if (seconds <= 0 || bucketSeconds <= 0 || hourWidth <= 0 || scale <= 0)
        {
            return 0;
        }

        double width = hourWidth * (seconds / bucketSeconds) * scale;
        if (width <= 0)
        {
            return 0;
        }

        accumulator.AddData(
            width,
            colorHex,
            $"{appName}|{state}",
            $"{appName} | {state}",
            localStart,
            localEnd,
            seconds);
        return width;
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

                string tooltip = $"{segment.TooltipPrefix} | {segment.LocalStart:HH\\:mm}-{segment.LocalEnd:HH\\:mm} | {ToDuration(segment.Seconds)}";
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
                    string tooltip = $"{_state} | {app} | {column.LocalStart:HH\\:mm}-{column.LocalEnd:HH\\:mm} | {ToDuration(seconds)}";
                    entries.Add(new StackedEntryLayout(
                        app,
                        column.ColorByApp.GetValueOrDefault(app, OtherColorHex),
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

    private static string FormatBucketLabel(DateTimeOffset bucketStartUtc, DateTimeOffset bucketEndUtc)
    {
        DateTimeOffset localStart = bucketStartUtc.ToLocalTime();
        DateTimeOffset localEnd = bucketEndUtc.ToLocalTime();
        TimeSpan bucket = bucketEndUtc - bucketStartUtc;
        if (bucket >= TimeSpan.FromHours(23))
        {
            return $"{localStart:MM/dd (ddd)}";
        }

        return $"{localStart:MM/dd HH:mm} - {localEnd:HH:mm}";
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

    private static string ToDuration(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}";
    }

    private static uint ComputeStableHash(string key)
    {
        uint hash = 2166136261;
        foreach (char c in key.ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h * 6) % 2 - 1));
        double m = l - c / 2;

        double r1;
        double g1;
        double b1;

        if (h < 1.0 / 6.0)
        {
            r1 = c;
            g1 = x;
            b1 = 0d;
        }
        else if (h < 2.0 / 6.0)
        {
            r1 = x;
            g1 = c;
            b1 = 0d;
        }
        else if (h < 3.0 / 6.0)
        {
            r1 = 0d;
            g1 = c;
            b1 = x;
        }
        else if (h < 4.0 / 6.0)
        {
            r1 = 0d;
            g1 = x;
            b1 = c;
        }
        else if (h < 5.0 / 6.0)
        {
            r1 = x;
            g1 = 0d;
            b1 = c;
        }
        else
        {
            r1 = c;
            g1 = 0d;
            b1 = x;
        }

        byte r = (byte)Math.Round((r1 + m) * 255);
        byte g = (byte)Math.Round((g1 + m) * 255);
        byte b = (byte)Math.Round((b1 + m) * 255);
        return (r, g, b);
    }
}
