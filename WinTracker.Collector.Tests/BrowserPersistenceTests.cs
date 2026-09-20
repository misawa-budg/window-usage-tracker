using Microsoft.Data.Sqlite;
using WinTracker.Shared.Analytics;
using WinTracker.Viewer;
using Xunit;

namespace WinTracker.Collector.Tests;

[CollectionDefinition("Browser storage", DisableParallelization = true)]
public sealed class BrowserStorageCollection { }

[Collection("Browser storage")]
public sealed class BrowserPersistenceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-20T00:00:00Z");

    [Fact]
    public void BrowserDemoHasBoundedSyntheticServicesAndNoOverlappingForeground()
    {
        var rows = DummySeedConsole.NormalizeActiveIntervals(DummySeedConsole.BuildServiceEvents(Start, Start.AddDays(7)));
        Assert.All(rows, row => { Assert.Equal("demo-seed", row.Source); Assert.Empty(row.Title); });
        Assert.Equal(new[] { "gmail", "github", "youtube", "twitch", "other-web" },
            rows.Where(row => row.ServiceId is not null).Select(row => row.ServiceId).Distinct());
        var active = rows.Where(row => row.State == "Active").ToArray();
        Assert.Equal(56 * 3600, active.Sum(row => (row.StateEndUtc - row.StateStartUtc).TotalSeconds));
        for (int i = 1; i < active.Length; i++) Assert.True(active[i - 1].StateEndUtc <= active[i].StateStartUtc);
    }

    [Fact]
    public void ServiceChangesSplitAnOtherwiseUnchangedAppInterval()
    {
        var writer = new Recorder();
        var tracker = new AppIntervalTracker(writer);
        foreach (var (id, seconds) in new (string?, int)[] { ("youtube", 0), ("gmail", 10), (null, 20) })
            tracker.ApplySnapshot(new Dictionary<string, AppSnapshot>
            { ["msedge.exe"] = new("msedge.exe", 1, "0x1", "", "Active", id) }, Start.AddSeconds(seconds), "test");
        tracker.Checkpoint(Start.AddSeconds(30), "test", close: true);
        Assert.Equal(new string?[] { "youtube", "gmail", null }, writer.Events.Select(x => x.ServiceId));
        Assert.All(writer.Events, x => Assert.Equal(10, (x.StateEndUtc - x.StateStartUtc).TotalSeconds));
    }

    [Fact]
    public void ProjectionReplacesBrowserTotalsAndKeepsUnknownSeparateFromOtherWeb()
    {
        AppStateIntervalRow[] raw = [
            new("msedge.exe", "Active", Start, Start.AddSeconds(10), "youtube"),
            new("chrome.exe", "Active", Start.AddSeconds(10), Start.AddSeconds(20), "youtube"),
            new("msedge.exe", "Active", Start.AddSeconds(20), Start.AddSeconds(30)),
            new("msedge.exe", "Active", Start.AddSeconds(30), Start.AddSeconds(40), "other-web"),
            new("editor.exe", "Active", Start.AddSeconds(40), Start.AddSeconds(50)),
            new("chrome.exe", "Open", Start, Start.AddSeconds(50))];
        var rows = BrowserServices.ProjectForeground(raw);
        Assert.Equal(50, rows.Sum(x => (x.StateEndUtc - x.StateStartUtc).TotalSeconds));
        Assert.Equal(2, rows.Count(x => x.ExeName == "service:youtube"));
        Assert.Contains(rows, x => x.ExeName == "browser:edge:unknown");
        Assert.Contains(rows, x => x.ExeName == "service:other-web");
        Assert.Equal("YouTube", AppChoice.FormatDisplayName("service:youtube"));
    }

    [Fact]
    public void LegacyDatabaseCanBeReadThenMigratedWithoutRewritingHistory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wintracker-services-{Guid.NewGuid():N}.db");
        try
        {
            using (var writer = new SqliteEventWriter(path))
                writer.Write(new(Start, Start.AddSeconds(10), "msedge.exe", 1, "0x1", "", "Active", "test"));
            using (var db = new SqliteConnection($"Data Source={path}"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "ALTER TABLE app_events DROP COLUMN service_id;";
                cmd.ExecuteNonQuery();
            }
            var window = new UsageQueryWindow(Start, Start.AddMinutes(1), TimeSpan.FromSeconds(10));
            using (var old = new SqliteTimelineQueryService(path))
                Assert.Null(Assert.Single(old.QueryStateIntervals(window)).ServiceId);
            using (var writer = new SqliteEventWriter(path))
            {
                writer.Write(new(Start.AddSeconds(10), Start.AddSeconds(20), "chrome.exe", 1, "0x2", "secret", "Active", "test", "gmail"));
                writer.Write(new(Start.AddSeconds(20), Start.AddSeconds(30), "chrome.exe", 1, "0x2", "secret", "Active", "test", "https://private.example/path"));
            }
            using var query = new SqliteTimelineQueryService(path);
            Assert.Equal(new string?[] { null, "gmail", null }, query.QueryStateIntervals(window).Select(x => x.ServiceId));
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM app_events WHERE title <> '';";
            Assert.Equal(0L, check.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private sealed class Recorder : IAppEventWriter
    {
        public List<AppEvent> Events { get; } = [];
        public void Write(AppEvent value) => Events.Add(value);
        public void Flush() { }
        public void Dispose() { }
    }
}
