using System.Threading.Channels;
using Xunit;
using static WinTracker.Collector.Tests.CollectorPersistenceTests;

namespace WinTracker.Collector.Tests;

public sealed class CollectorSessionTests
{
    [Fact]
    public async Task SleepGapIsNotCountedAsUsage()
    {
        var writer = await Run(new[] { 0, 15, 3615, 3630 });
        Assert.Equal(30, writer.Events.Sum(e => (e.StateEndUtc - e.StateStartUtc).TotalSeconds));
        Assert.DoesNotContain(writer.Events, e => e.StateStartUtc < Start.AddSeconds(3615) && e.StateEndUtc > Start.AddSeconds(15));
    }

    [Fact]
    public async Task LockedDesktopClosesIntervalsAndUnlockStartsFresh()
    {
        var available = new Queue<bool>(new[] { true, true, false, false, true, true });
        var writer = await Run(new[] { 0, 15, 30, 45, 60, 75 }, () => available.Dequeue());
        Assert.Equal(30, writer.Events.Sum(e => (e.StateEndUtc - e.StateStartUtc).TotalSeconds));
        Assert.DoesNotContain(writer.Events, e => e.StateStartUtc < Start.AddSeconds(60) && e.StateEndUtc > Start.AddSeconds(15));
    }

    [Fact]
    public async Task BackwardsClockDoesNotWriteNegativeOrOverlappingIntervals()
    {
        var writer = await Run(new[] { 0, 15, 5, 10, 20, 35 });
        Assert.All(writer.Events, e => Assert.True(e.StateEndUtc > e.StateStartUtc));
        for (int i = 1; i < writer.Events.Count; i++)
            Assert.True(writer.Events[i].StateStartUtc >= writer.Events[i - 1].StateEndUtc);
    }

    private static async Task<RecordingWriter> Run(int[] seconds, Func<bool>? available = null)
    {
        var channel = Channel.CreateUnbounded<CollectReason>();
        foreach (int _ in seconds) channel.Writer.TryWrite(CollectReason.Checkpoint);
        channel.Writer.Complete();
        var writer = new RecordingWriter();
        var clock = new SequenceClock(new[] { Start }.Concat(seconds.Select(second => Start.AddSeconds(second))));
        await ForegroundCollector.RunLoopAsync(channel.Reader, CancellationToken.None, writer,
            new CollectorSettings(), () => Snapshot("Active"), clock, sessionAvailable: available);
        return writer;
    }

    private sealed class SequenceClock(IEnumerable<DateTimeOffset> values) : TimeProvider
    {
        private readonly Queue<DateTimeOffset> _values = new(values);
        public override DateTimeOffset GetUtcNow() => _values.Dequeue();
    }
}
