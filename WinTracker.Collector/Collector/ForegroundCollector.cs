using System.Threading.Channels;

internal static class ForegroundCollector
{
    public static async Task RunEventDrivenAsync(CancellationToken cancellationToken,
        IAppEventWriter eventWriter, CollectorSettings settings)
    {
        var excluded = new HashSet<string>(settings.ExcludedExeNames, StringComparer.OrdinalIgnoreCase);
        // Signals request fresh snapshots, not individual events; bound pending work.
        var signals = Channel.CreateBounded<CollectReason>(new BoundedChannelOptions(1)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
        });
        int captureRequested = 1;
        using var hookPump = new WinEventHookPump(reason =>
        {
            Interlocked.Exchange(ref captureRequested, 1);
            signals.Writer.TryWrite(reason);
        });
        hookPump.Start();
        signals.Writer.TryWrite(CollectReason.Startup);
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.CheckpointIntervalSeconds));
        Task producer = ProduceTicksAsync();
        try
        {
            await RunLoopAsync(signals.Reader, cancellationToken, eventWriter, settings,
                () => WindowSnapshotProvider.CaptureCurrentStates(excluded),
                scanRequested: () => Interlocked.Exchange(ref captureRequested, 0) != 0);
        }
        finally
        {
            producerCancellation.Cancel();
            await producer;
        }

        async Task ProduceTicksAsync()
        {
            try
            {
                while (await timer.WaitForNextTickAsync(producerCancellation.Token))
                    signals.Writer.TryWrite(CollectReason.Checkpoint);
            }
            catch (OperationCanceledException) when (producerCancellation.IsCancellationRequested) { }
        }
    }

    // Keep the OS adapter separate so failures and time boundaries can be tested.
    internal static async Task RunLoopAsync(ChannelReader<CollectReason> signals,
        CancellationToken cancellationToken, IAppEventWriter writer, CollectorSettings settings,
        Func<Dictionary<string, AppSnapshot>> capture, TimeProvider? clock = null,
        Func<bool>? scanRequested = null)
    {
        clock ??= TimeProvider.System;
        var tracker = new AppIntervalTracker(writer);
        DateTimeOffset lastCheckpoint = clock.GetUtcNow();
        DateTimeOffset lastScan = DateTimeOffset.MinValue;
        DateTimeOffset lastObserved = lastCheckpoint;
        try
        {
            await foreach (CollectReason reason in signals.ReadAllAsync(cancellationToken))
            {
                DateTimeOffset now = clock.GetUtcNow();
                bool needsScan = scanRequested?.Invoke() ?? false;
                if (needsScan || reason != CollectReason.Checkpoint ||
                    now - lastScan >= TimeSpan.FromSeconds(settings.RescanIntervalSeconds))
                {
                    tracker.ApplySnapshot(capture(), now, reason == CollectReason.Checkpoint ? "rescan" : "win_event");
                    lastScan = now;
                }
                lastObserved = now;
                if (now - lastCheckpoint >= TimeSpan.FromSeconds(settings.CheckpointIntervalSeconds))
                {
                    tracker.Checkpoint(now, "checkpoint");
                    lastCheckpoint = now;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            // Never extend a failed capture into unobserved time.
            tracker.Checkpoint(lastObserved, "shutdown", close: true);
        }
    }
}
