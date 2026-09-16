// Only the collector loop accesses this state and its writer.
internal sealed class AppIntervalTracker(IAppEventWriter writer)
{
    private readonly Dictionary<string, AppInterval> _intervals = new(StringComparer.OrdinalIgnoreCase);

    public void ApplySnapshot(IReadOnlyDictionary<string, AppSnapshot> current, DateTimeOffset now, string source)
    {
        foreach ((string key, AppSnapshot snapshot) in current)
        {
            if (_intervals.TryGetValue(key, out AppInterval existing))
            {
                if (existing.State == snapshot.State)
                {
                    _intervals[key] = existing with
                    {
                        StateEndUtc = now, Pid = snapshot.Pid, Hwnd = snapshot.Hwnd, Title = snapshot.Title
                    };
                    continue;
                }
                Write(existing, now, source);
            }
            _intervals[key] = new AppInterval(now, now, snapshot.ExeName, snapshot.Pid,
                snapshot.Hwnd, snapshot.Title, snapshot.State);
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
            AppInterval interval = _intervals[key];
            Write(interval, now, source);
            _intervals[key] = interval with { StateStartUtc = now, StateEndUtc = now };
        }
        writer.Flush();
        if (close) _intervals.Clear();
    }

    private void Write(AppInterval interval, DateTimeOffset end, string source)
    {
        if (end <= interval.StateStartUtc) return;
        writer.Write(new AppEvent(interval.StateStartUtc, end, interval.ExeName, interval.Pid,
            interval.Hwnd, interval.Title, interval.State, source));
    }
}
