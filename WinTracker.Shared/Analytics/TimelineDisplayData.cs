namespace WinTracker.Shared.Analytics;

public enum AppDisplayMode { Services, Running, StateDetails }

// One snapshot per reload/mode change, shared by the overview, selector and app rows.
public sealed record TimelineDisplayData(
    IReadOnlyList<AppStateIntervalRow> AppIntervals,
    IReadOnlyList<ActiveIntervalRow> ActiveIntervals)
{
    public static TimelineDisplayData Create(IReadOnlyList<AppStateIntervalRow> source, AppDisplayMode mode)
    {
        IReadOnlyList<AppStateIntervalRow> displayed = mode switch
        {
            AppDisplayMode.Services => BrowserServices.ProjectForeground(source),
            AppDisplayMode.StateDetails => source,
            AppDisplayMode.Running => source.Select(x =>
                new AppStateIntervalRow(x.ExeName, "Running", x.StateStartUtc, x.StateEndUtc)).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        // Running only combines the lower rows; the overview must remain foreground-only.
        var foreground = mode == AppDisplayMode.Services ? displayed : source;
        return new(displayed, foreground.Where(x => x.State == "Active")
            .Select(x => new ActiveIntervalRow(x.ExeName, x.StateStartUtc, x.StateEndUtc)).ToArray());
    }
}
