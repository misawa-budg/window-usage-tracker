internal readonly record struct AppStateUsageRow(
    string ExeName,
    string State,
    double Seconds);

internal readonly record struct AppUsageSummaryRow(
    string ExeName,
    double TotalSeconds,
    double ActiveSeconds,
    double OpenSeconds,
    double MinimizedSeconds);

internal readonly record struct TimelineUsageRow(
    DateTimeOffset BucketStartUtc,
    string ExeName,
    string State,
    double Seconds);
