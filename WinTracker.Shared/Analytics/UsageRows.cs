namespace WinTracker.Shared.Analytics;

public readonly record struct AppStateUsageRow(
    string ExeName,
    string State,
    double Seconds);

public readonly record struct AppUsageSummaryRow(
    string ExeName,
    double TotalSeconds,
    double ActiveSeconds,
    double OpenSeconds,
    double MinimizedSeconds);

public readonly record struct TimelineUsageRow(
    DateTimeOffset BucketStartUtc,
    string ExeName,
    string State,
    double Seconds);

public readonly record struct ActiveIntervalRow(
    string ExeName,
    DateTimeOffset StateStartUtc,
    DateTimeOffset StateEndUtc);

public readonly record struct AppStateIntervalRow(
    string ExeName,
    string State,
    DateTimeOffset StateStartUtc,
    DateTimeOffset StateEndUtc);
