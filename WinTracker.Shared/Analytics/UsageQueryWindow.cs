namespace WinTracker.Shared.Analytics;

public readonly record struct UsageQueryWindow(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    TimeSpan BucketSize)
{
    public int BucketSeconds => (int)BucketSize.TotalSeconds;

    public void Validate()
    {
        if (ToUtc <= FromUtc) throw new ArgumentException("Query end must be after start.");
        if (BucketSize.TotalSeconds < 1 || BucketSize.TotalSeconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(BucketSize), "Bucket size must be at least one second.");
    }

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
