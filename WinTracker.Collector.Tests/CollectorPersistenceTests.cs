using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace WinTracker.Collector.Tests;

public sealed class CollectorPersistenceTests
{
    internal static readonly DateTimeOffset Start = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CheckpointsPersistUnchangedStateWithoutGapsOrOverlap()
    {
        var writer = new RecordingWriter();
        var tracker = new AppIntervalTracker(writer);
        tracker.ApplySnapshot(Snapshot("Active"), Start, "test");
        tracker.Checkpoint(Start.AddSeconds(15), "checkpoint");
        tracker.Checkpoint(Start.AddSeconds(30), "checkpoint");
        tracker.ApplySnapshot(Snapshot("Open"), Start.AddSeconds(35), "test");
        tracker.Checkpoint(Start.AddSeconds(40), "shutdown", close: true);
        tracker.Checkpoint(Start.AddSeconds(50), "shutdown", close: true);
        Assert.Equal(4, writer.Events.Count);
        Assert.Equal(new[] { 15d, 15d, 5d, 5d }, writer.Events.Select(e => (e.StateEndUtc - e.StateStartUtc).TotalSeconds));
        for (int i = 1; i < writer.Events.Count; i++)
            Assert.Equal(writer.Events[i - 1].StateEndUtc, writer.Events[i].StateStartUtc);
    }

    [Fact]
    public void FlushMakesSmallBatchesVisibleToAnotherConnection()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wintracker-{Guid.NewGuid():N}.db");
        try
        {
            using var writer = new SqliteEventWriter(path);
            var tracker = new AppIntervalTracker(writer);
            tracker.ApplySnapshot(Snapshot("Active"), Start, "test");
            tracker.Checkpoint(Start.AddSeconds(15), "checkpoint");
            using var reader = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            reader.Open();
            using var command = reader.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM app_events";
            Assert.Equal(1L, command.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task CaptureFailureTerminatesWithoutExternalCancellation()
    {
        var signals = Channel.CreateUnbounded<CollectReason>();
        signals.Writer.TryWrite(CollectReason.Startup);
        Task run = ForegroundCollector.RunLoopAsync(signals.Reader, CancellationToken.None,
            new RecordingWriter(), new CollectorSettings(), () => throw new IOException("Synthetic capture failure"));
        await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    internal static Dictionary<string, AppSnapshot> Snapshot(string state) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["editor.exe"] = new AppSnapshot("editor.exe", 1, "0x1", "test", state)
    };

    internal sealed class RecordingWriter : IAppEventWriter
    {
        public List<AppEvent> Events { get; } = [];
        public void Write(AppEvent e) => Events.Add(e);
        public void Flush() { }
        public void Dispose() { }
    }
}
