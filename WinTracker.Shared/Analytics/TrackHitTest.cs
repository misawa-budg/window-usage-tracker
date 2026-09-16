namespace WinTracker.Shared.Analytics;

public static class TrackHitTest
{
    // Cumulative right edges, sorted ascending. Intervals are half-open [left, right).
    public static int FindColumn(IReadOnlyList<double> rightEdges, double x)
    {
        ArgumentNullException.ThrowIfNull(rightEdges);
        if (!double.IsFinite(x) || x < 0 || rightEdges.Count == 0 || x >= rightEdges[^1])
            return -1;

        int low = 0, high = rightEdges.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (x < rightEdges[middle]) high = middle;
            else low = middle + 1;
        }
        return low;
    }
}
