namespace WinTracker.Shared.Analytics;

public readonly record struct UsageQueryWindow(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    TimeSpan BucketSize)
{
    public int BucketSeconds => (int)BucketSize.TotalSeconds;

    public static UsageQueryWindow Last24Hours(DateTimeOffset nowUtc) =>
        new(
            FromUtc: nowUtc.AddHours(-24),
            ToUtc: nowUtc,
            BucketSize: TimeSpan.FromHours(1));

    public static UsageQueryWindow Last7Days(DateTimeOffset nowUtc) =>
        new(
            FromUtc: nowUtc.AddDays(-7),
            ToUtc: nowUtc,
            BucketSize: TimeSpan.FromDays(1));
}
