using System.Text.Json;
using System.Threading.Channels;

internal static class ForegroundCollector
{
    public static async Task RunEventDrivenAsync(
        CancellationToken cancellationToken,
        IAppEventWriter eventWriter,
        CollectorSettings settings)
    {
        var excludedExeNames = new HashSet<string>(settings.ExcludedExeNames, StringComparer.OrdinalIgnoreCase);
        var intervalsByApp = new Dictionary<string, AppInterval>(StringComparer.OrdinalIgnoreCase);
        var signals = Channel.CreateUnbounded<CollectReason>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        using var hookPump = new WinEventHookPump(reason => _ = signals.Writer.TryWrite(reason));
        hookPump.Start();
        _ = signals.Writer.TryWrite(CollectReason.Startup);

        using var rescanTimer = new PeriodicTimer(TimeSpan.FromSeconds(settings.RescanIntervalSeconds));
        Task rescanTask = Task.Run(async () =>
        {
            try
            {
                while (await rescanTimer.WaitForNextTickAsync(cancellationToken))
                {
                    _ = signals.Writer.TryWrite(CollectReason.Rescan);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CollectReason reason;
                try
                {
                    reason = await signals.Reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                while (signals.Reader.TryRead(out CollectReason nextReason))
                {
                    reason = nextReason;
                }

                DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
                Dictionary<string, AppSnapshot> currentByApp = WindowSnapshotProvider.CaptureCurrentStates(excludedExeNames);
                string source = reason == CollectReason.Rescan ? "rescan" : "win_event";
                ApplySnapshot(currentByApp, observedAtUtc, source, intervalsByApp, eventWriter);
            }
        }
        finally
        {
            DateTimeOffset stoppedAtUtc = DateTimeOffset.UtcNow;
            foreach (AppInterval intervalState in intervalsByApp.Values)
            {
                WriteClosedInterval(eventWriter, intervalState with { StateEndUtc = stoppedAtUtc }, "shutdown");
            }

            try
            {
                await rescanTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static void ApplySnapshot(
        Dictionary<string, AppSnapshot> currentByApp,
        DateTimeOffset observedAtUtc,
        string source,
        Dictionary<string, AppInterval> intervalsByApp,
        IAppEventWriter eventWriter)
    {
        foreach ((string appKey, AppSnapshot current) in currentByApp)
        {
            if (!intervalsByApp.TryGetValue(appKey, out AppInterval existing))
            {
                intervalsByApp[appKey] = new AppInterval(
                    StateStartUtc: observedAtUtc,
                    StateEndUtc: observedAtUtc,
                    ExeName: current.ExeName,
                    Pid: current.Pid,
                    Hwnd: current.Hwnd,
                    Title: current.Title,
                    State: current.State);
                continue;
            }

            if (string.Equals(existing.State, current.State, StringComparison.Ordinal))
            {
                intervalsByApp[appKey] = existing with
                {
                    StateEndUtc = observedAtUtc,
                    Pid = current.Pid,
                    Hwnd = current.Hwnd,
                    Title = current.Title
                };
                continue;
            }

            AppInterval closedInterval = existing with { StateEndUtc = observedAtUtc };
            WriteClosedInterval(eventWriter, closedInterval, source);

            intervalsByApp[appKey] = new AppInterval(
                StateStartUtc: observedAtUtc,
                StateEndUtc: observedAtUtc,
                ExeName: current.ExeName,
                Pid: current.Pid,
                Hwnd: current.Hwnd,
                Title: current.Title,
                State: current.State);
        }

        foreach (string removedKey in intervalsByApp.Keys.Except(currentByApp.Keys).ToList())
        {
            AppInterval closedInterval = intervalsByApp[removedKey] with { StateEndUtc = observedAtUtc };
            WriteClosedInterval(eventWriter, closedInterval, source);
            intervalsByApp.Remove(removedKey);
        }
    }

    private static void WriteClosedInterval(IAppEventWriter eventWriter, AppInterval interval, string source)
    {
        if (interval.StateEndUtc < interval.StateStartUtc)
        {
            return;
        }

        var appEvent = new AppEvent(
            StateStartUtc: interval.StateStartUtc,
            StateEndUtc: interval.StateEndUtc,
            ExeName: interval.ExeName,
            Pid: interval.Pid,
            Hwnd: interval.Hwnd,
            Title: interval.Title,
            State: interval.State,
            Source: source);

        eventWriter.Write(appEvent);
        Console.WriteLine(JsonSerializer.Serialize(appEvent));
    }
}
