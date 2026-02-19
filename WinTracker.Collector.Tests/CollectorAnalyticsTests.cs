using Microsoft.Data.Sqlite;
using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Collector.Tests;

public sealed class CollectorAnalyticsTests
{
    [Fact]
    public void SqliteEventWriter_CreatesSchemaAndFlushesBufferedEventsOnDispose()
    {
        string dbPath = CreateTempDatabasePath();

        try
        {
            DateTimeOffset startUtc = Utc(2026, 2, 19, 10, 0, 0);
            DateTimeOffset endUtc = startUtc.AddMinutes(15);

            using (var writer = new SqliteEventWriter(dbPath))
            {
                writer.Write(CreateEvent(
                    startUtc,
                    endUtc,
                    exeName: "devenv.exe",
                    state: "Active"));
            }

            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Cache=Shared");
            connection.Open();

            long rowCount = ExecuteScalar<long>(connection, "SELECT COUNT(*) FROM app_events;");
            Assert.Equal(1, rowCount);

            long indexCount = ExecuteScalar<long>(
                connection,
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'index'
                  AND name IN ('idx_app_events_time', 'idx_app_events_exe_time');
                """);
            Assert.Equal(2, indexCount);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void QueryStateTotals_ClipsIntervalsToWindowBoundaries()
    {
        string dbPath = CreateTempDatabasePath();

        try
        {
            DateTimeOffset t1000 = Utc(2026, 2, 19, 10, 0, 0);

            SeedEvents(
                dbPath,
                CreateEvent(t1000, t1000.AddHours(1), "devenv.exe", "Active"),
                CreateEvent(t1000.AddHours(1), t1000.AddHours(2), "devenv.exe", "Open"),
                CreateEvent(t1000.AddMinutes(-30), t1000.AddMinutes(15), "powershell.exe", "Active"),
                CreateEvent(t1000.AddHours(4), t1000.AddHours(5), "msedge.exe", "Open"));

            UsageQueryWindow window = new(
                FromUtc: t1000,
                ToUtc: t1000.AddHours(1.5),
                BucketSize: TimeSpan.FromHours(1));

            using var query = new SqliteUsageQueryService(dbPath);
            IReadOnlyList<AppStateUsageRow> rows = query.QueryStateTotals(window);

            AssertSeconds(rows, "devenv.exe", "Active", 3600);
            AssertSeconds(rows, "devenv.exe", "Open", 1800);
            AssertSeconds(rows, "powershell.exe", "Active", 900);
            Assert.DoesNotContain(rows, r => string.Equals(r.ExeName, "msedge.exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void QueryAppSummaries_AggregatesPerAppAndState()
    {
        string dbPath = CreateTempDatabasePath();

        try
        {
            DateTimeOffset t1000 = Utc(2026, 2, 19, 10, 0, 0);

            SeedEvents(
                dbPath,
                CreateEvent(t1000, t1000.AddHours(1), "devenv.exe", "Active"),
                CreateEvent(t1000.AddHours(1), t1000.AddHours(1.5), "devenv.exe", "Open"),
                CreateEvent(t1000.AddHours(1.5), t1000.AddHours(2), "devenv.exe", "Minimized"),
                CreateEvent(t1000, t1000.AddHours(2), "powershell.exe", "Open"));

            UsageQueryWindow window = new(
                FromUtc: t1000,
                ToUtc: t1000.AddHours(2),
                BucketSize: TimeSpan.FromHours(1));

            using var query = new SqliteUsageQueryService(dbPath);
            IReadOnlyList<AppUsageSummaryRow> rows = query.QueryAppSummaries(window);

            AppUsageSummaryRow devenv = rows.Single(r => string.Equals(r.ExeName, "devenv.exe", StringComparison.OrdinalIgnoreCase));
            AssertApproximately(devenv.TotalSeconds, 7200);
            AssertApproximately(devenv.ActiveSeconds, 3600);
            AssertApproximately(devenv.OpenSeconds, 1800);
            AssertApproximately(devenv.MinimizedSeconds, 1800);

            AppUsageSummaryRow powershell = rows.Single(r => string.Equals(r.ExeName, "powershell.exe", StringComparison.OrdinalIgnoreCase));
            AssertApproximately(powershell.TotalSeconds, 7200);
            AssertApproximately(powershell.ActiveSeconds, 0);
            AssertApproximately(powershell.OpenSeconds, 7200);
            AssertApproximately(powershell.MinimizedSeconds, 0);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void QueryTimeline_SplitsIntervalsIntoHourlyBuckets()
    {
        string dbPath = CreateTempDatabasePath();

        try
        {
            DateTimeOffset t1000 = Utc(2026, 2, 19, 10, 0, 0);

            SeedEvents(
                dbPath,
                CreateEvent(t1000.AddMinutes(30), t1000.AddHours(2.25), "devenv.exe", "Active"),
                CreateEvent(t1000.AddHours(1.75), t1000.AddHours(2.25), "powershell.exe", "Open"));

            UsageQueryWindow window = new(
                FromUtc: t1000,
                ToUtc: t1000.AddHours(3),
                BucketSize: TimeSpan.FromHours(1));

            using var query = new SqliteUsageQueryService(dbPath);
            IReadOnlyList<TimelineUsageRow> rows = query.QueryTimeline(window);

            AssertTimelineSeconds(rows, t1000, "devenv.exe", "Active", 1800);
            AssertTimelineSeconds(rows, t1000.AddHours(1), "devenv.exe", "Active", 3600);
            AssertTimelineSeconds(rows, t1000.AddHours(2), "devenv.exe", "Active", 900);
            AssertTimelineSeconds(rows, t1000.AddHours(1), "powershell.exe", "Open", 900);
            AssertTimelineSeconds(rows, t1000.AddHours(2), "powershell.exe", "Open", 900);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void UsageQueryWindow_FactoryMethodsUseExpectedRangeAndBucket()
    {
        DateTimeOffset nowUtc = Utc(2026, 2, 19, 12, 0, 0);

        UsageQueryWindow day = UsageQueryWindow.Last24Hours(nowUtc);
        Assert.Equal(TimeSpan.FromHours(1), day.BucketSize);
        Assert.Equal(nowUtc.AddHours(-24), day.FromUtc);
        Assert.Equal(nowUtc, day.ToUtc);

        UsageQueryWindow week = UsageQueryWindow.Last7Days(nowUtc);
        Assert.Equal(TimeSpan.FromDays(1), week.BucketSize);
        Assert.Equal(nowUtc.AddDays(-7), week.FromUtc);
        Assert.Equal(nowUtc, week.ToUtc);
    }

    private static void SeedEvents(string dbPath, params AppEvent[] events)
    {
        using var writer = new SqliteEventWriter(dbPath);
        foreach (AppEvent appEvent in events)
        {
            writer.Write(appEvent);
        }
    }

    private static AppEvent CreateEvent(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string exeName,
        string state) =>
        new(
            StateStartUtc: startUtc,
            StateEndUtc: endUtc,
            ExeName: exeName,
            Pid: 1234,
            Hwnd: "0x123456",
            Title: exeName,
            State: state,
            Source: "test");

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

    private static string CreateTempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"wintracker-collector-tests-{Guid.NewGuid():N}.db");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static T ExecuteScalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private static void AssertSeconds(
        IReadOnlyList<AppStateUsageRow> rows,
        string exeName,
        string state,
        double expectedSeconds)
    {
        AppStateUsageRow row = rows.Single(r =>
            string.Equals(r.ExeName, exeName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.State, state, StringComparison.OrdinalIgnoreCase));

        AssertApproximately(row.Seconds, expectedSeconds);
    }

    private static void AssertTimelineSeconds(
        IReadOnlyList<TimelineUsageRow> rows,
        DateTimeOffset bucketStartUtc,
        string exeName,
        string state,
        double expectedSeconds)
    {
        TimelineUsageRow row = rows.Single(r =>
            r.BucketStartUtc == bucketStartUtc &&
            string.Equals(r.ExeName, exeName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.State, state, StringComparison.OrdinalIgnoreCase));

        AssertApproximately(row.Seconds, expectedSeconds);
    }

    private static void AssertApproximately(double actual, double expected)
    {
        const double tolerance = 1.0;
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
}
