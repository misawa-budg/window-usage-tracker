using Microsoft.Data.Sqlite;
using WinTracker.Shared.Analytics;
using WinTracker.Viewer;
using Xunit;

namespace WinTracker.Collector.Tests;

public sealed class QueryIntegrationTests
{
    [Fact]
    public void NormalQueriesExcludeLegacySeedRowsAndDemoQueriesIncludeThem()
    {
        WithDatabase((path, start) =>
        {
            var window = new UsageQueryWindow(start, start.AddHours(3), TimeSpan.FromHours(1));
            using var collector = new SqliteUsageQueryService(path);
            using var viewer = new SqliteTimelineQueryService(path);
            using var demo = new SqliteTimelineQueryService(path, includeDemo: true);
            Assert.Single(collector.QueryAppSummaries(window));
            Assert.Single(collector.QueryStateTotals(window));
            Assert.All(collector.QueryTimeline(window), row => Assert.Equal("real.exe", row.ExeName));
            Assert.Single(viewer.QueryActiveIntervals(window));
            Assert.Single(viewer.QueryStateIntervals(window));
            Assert.All(viewer.QueryTimeline(window), row => Assert.Equal("real.exe", row.ExeName));
            Assert.Equal(2, demo.QueryStateIntervals(window).Count);
        });
    }

    private static void WithDatabase(Action<string, DateTimeOffset> test)
    {
        string path = Path.Combine(Path.GetTempPath(), $"wintracker-query-{Guid.NewGuid():N}.db");
        var start = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        try
        {
            using (var writer = new SqliteEventWriter(path))
            {
                writer.Write(new AppEvent(start, start.AddHours(3), "real.exe", 1, "0x1", "", "Active", "test"));
                writer.Write(new AppEvent(start, start.AddHours(3), "seed.exe", 2, "0x2", "", "Active", "demo-seed"));
            }
            test(path, start);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
