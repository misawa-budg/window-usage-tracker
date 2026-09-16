// Only the collector loop accesses this state and its writer.
internal sealed class AppIntervalTracker(IAppEventWriter writer)
{
    private readonly Dictionary<string, TrackedInterval> _intervals = new(StringComparer.OrdinalIgnoreCase);

    // An ongoing interval has a start and the latest snapshot, but no final end yet.
    private readonly record struct TrackedInterval(DateTimeOffset StartUtc, AppSnapshot Snapshot);

    public void ApplySnapshot(IReadOnlyDictionary<string, AppSnapshot> current, DateTimeOffset now, string source)
    {
        foreach ((string key, AppSnapshot snapshot) in current)
        {
            if (_intervals.TryGetValue(key, out TrackedInterval existing))
            {
                if (existing.Snapshot.State == snapshot.State)
                {
                    _intervals[key] = existing with
                    {
                        Snapshot = snapshot with { ExeName = existing.Snapshot.ExeName }
                    };
                    continue;
                }
                Write(existing, now, source);
            }
            _intervals[key] = new TrackedInterval(now, snapshot);
        }
        foreach (string key in _intervals.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            Write(_intervals[key], now, source);
            _intervals.Remove(key);
        }
    }

    public void Checkpoint(DateTimeOffset now, string source, bool close = false)
    {
        foreach (string key in _intervals.Keys.ToArray())
        {
            TrackedInterval interval = _intervals[key];
            Write(interval, now, source);
            _intervals[key] = interval with { StartUtc = now };
        }
        writer.Flush();
        if (close) _intervals.Clear();
    }

    private void Write(TrackedInterval interval, DateTimeOffset end, string source)
    {
        if (end <= interval.StartUtc) return;
        AppSnapshot snapshot = interval.Snapshot;
        writer.Write(new AppEvent(interval.StartUtc, end, snapshot.ExeName, snapshot.Pid,
            snapshot.Hwnd, snapshot.Title, snapshot.State, source));
    }
}
