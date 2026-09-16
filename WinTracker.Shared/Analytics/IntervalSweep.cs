namespace WinTracker.Shared.Analytics;

internal readonly record struct IntervalSlice<T>(DateTimeOffset Start, DateTimeOffset End, T? Value) where T : struct;

internal static class IntervalSweep
{
    // Maintain the winning interval at each boundary instead of scanning all intervals per slice.
    // Equal-priority records retain separate identities so removing one cannot remove another.
    internal static IEnumerable<IntervalSlice<T>> Build<T>(IReadOnlyList<T> intervals,
        DateTimeOffset from, DateTimeOffset to, Func<T, DateTimeOffset> start,
        Func<T, DateTimeOffset> end, Comparison<T> priority) where T : struct
    {
        if (to <= from) yield break;
        var events = new SortedDictionary<DateTimeOffset, List<(int Index, bool Starts)>>
        {
            [from] = [], [to] = []
        };
        for (int index = 0; index < intervals.Count; index++)
        {
            DateTimeOffset a = start(intervals[index]), b = end(intervals[index]);
            a = a < from ? from : a;
            b = b > to ? to : b;
            if (b <= a) continue;
            if (!events.TryGetValue(a, out var starts)) events[a] = starts = [];
            if (!events.TryGetValue(b, out var ends)) events[b] = ends = [];
            starts.Add((index, true));
            ends.Add((index, false));
        }
        var active = new SortedSet<int>(Comparer<int>.Create((left, right) =>
        {
            int order = priority(intervals[left], intervals[right]);
            return order == 0 ? left.CompareTo(right) : order;
        }));
        DateTimeOffset[] boundaries = events.Keys.ToArray();
        for (int index = 0; index < boundaries.Length - 1; index++)
        {
            DateTimeOffset boundary = boundaries[index];
            foreach (var change in events[boundary])
            {
                if (change.Starts) active.Add(change.Index);
                else active.Remove(change.Index);
            }
            yield return new IntervalSlice<T>(boundary, boundaries[index + 1],
                active.Count == 0 ? null : intervals[active.Min]);
        }
    }
}
