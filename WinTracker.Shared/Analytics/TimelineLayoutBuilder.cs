namespace WinTracker.Shared.Analytics;

public sealed class TimelineLayoutBuilder
{
    public const string NoDataTooltip = "No data";
    public const string OtherLabel = "Other";
    public const string OtherColorHex = "#F8B5B5";
    public const string ActiveColorHex = "#BFA8FF";
    public const string OpenColorHex = "#B8E9C7";
    public const string MinimizedColorHex = "#F9E6A6";

    private static readonly string[] AppStates = ["Active", "Open", "Minimized"];

    private readonly int _topAppCount;

    public TimelineLayoutBuilder(int topAppCount = 8)
    {
        _topAppCount = Math.Max(1, topAppCount);
    }

    public IReadOnlyList<string> BuildAppNames(IReadOnlyList<TimelineUsageRow> timelineRows)
    {
        return timelineRows
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
        var apps = timelineRows
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

    public IReadOnlyList<TimelineRowLayout> BuildOverviewRows(
        IReadOnlyList<TimelineUsageRow> timelineRows,
        UsageQueryWindow window,
        double trackWidth)
    {
        var rows = new List<TimelineRowLayout>();
        var totalByApp = timelineRows
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
                List<TimelineUsageRow> hourRows = timelineRows
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
