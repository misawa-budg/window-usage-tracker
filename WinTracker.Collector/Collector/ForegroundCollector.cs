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
        void RequestCapture()
        {
            Interlocked.Exchange(ref captureRequested, 1);
            signals.Writer.TryWrite(CollectReason.WinEvent);
        }
        await using var browserHub = settings.EnableBrowserTracking
            ? new BrowserServiceHub(RequestCapture, WindowSnapshotProvider.CaptureBrowserForeground,
                storeBrowserHostnames: settings.StoreBrowserHostnames) : null;
        using var hookPump = new WinEventHookPump(reason =>
        {
            browserHub?.ForegroundChanged();
            Interlocked.Exchange(ref captureRequested, 1);
            signals.Writer.TryWrite(reason);
        }, error => signals.Writer.TryComplete(error));
        hookPump.Start();
        signals.Writer.TryWrite(CollectReason.Startup);
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.CheckpointIntervalSeconds));
        Task producer = ProduceTicksAsync();
        try
        {
            await RunLoopAsync(signals.Reader, cancellationToken, eventWriter, settings,
                () =>
                {
                    var snapshot = WindowSnapshotProvider.CaptureCurrentStates(excluded);
                    if (browserHub is not null)
                        foreach (string key in snapshot.Keys.ToArray())
                            snapshot[key] = snapshot[key] with { ServiceId = browserHub.GetService(snapshot[key], DateTimeOffset.UtcNow) };
                    return snapshot;
                },
                scanRequested: () => Interlocked.Exchange(ref captureRequested, 0) != 0,
                sessionAvailable: SessionAvailability.IsInputDesktop,
                observationInterrupted: () => browserHub?.Reset());
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
        Func<bool>? scanRequested = null, Func<bool>? sessionAvailable = null,
        Action? observationInterrupted = null)
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
                // Do not bridge sleep, long stalls, or backwards wall-clock adjustments.
                bool clockWentBackwards = now < lastObserved;
                if (clockWentBackwards || now - lastObserved > TimeSpan.FromSeconds(settings.CheckpointIntervalSeconds * 2))
                {
                    tracker.Checkpoint(lastObserved, "observation_gap", close: true);
                    observationInterrupted?.Invoke();
                    lastScan = DateTimeOffset.MinValue;
                    lastCheckpoint = now;
                    if (clockWentBackwards) continue;
                }
                if (sessionAvailable is not null && !sessionAvailable())
                {
                    tracker.Checkpoint(lastObserved, "session_unavailable", close: true);
                    observationInterrupted?.Invoke();
                    lastScan = DateTimeOffset.MinValue;
                    lastCheckpoint = lastObserved = now;
                    continue;
                }
                bool needsScan = scanRequested?.Invoke() ?? false;
                if (needsScan || reason != CollectReason.Checkpoint ||
                    now - lastScan >= TimeSpan.FromSeconds(settings.RescanIntervalSeconds))
                {
                    tracker.ApplySnapshot(capture(), now, reason == CollectReason.Checkpoint ? "rescan" : "win_event");
                    lastScan = now;
                }
                lastObserved = now;
                if (reason == CollectReason.Checkpoint ||
                    now - lastCheckpoint >= TimeSpan.FromSeconds(settings.CheckpointIntervalSeconds))
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
