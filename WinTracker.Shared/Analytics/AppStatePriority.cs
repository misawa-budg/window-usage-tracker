namespace WinTracker.Shared.Analytics;

// Shared by live window aggregation and the viewer's overlapping-interval resolution.
public static class AppStatePriority
{
    public static int Get(string state)
    {
        if (string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase)) return 3;
        if (string.Equals(state, "Open", StringComparison.OrdinalIgnoreCase)) return 2;
        if (string.Equals(state, "Minimized", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }
}
