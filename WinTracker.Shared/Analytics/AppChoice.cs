namespace WinTracker.Shared.Analytics;

// Keep the database key separate from its presentation; selection must never use a shortened name.
public sealed record AppChoice(string ExeName)
{
    public string DisplayName => FormatDisplayName(ExeName);

    public static string FormatDisplayName(string exeName) =>
        BrowserServices.DisplayName(exeName) ?? (exeName.Length > 4 && exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? exeName[..^4]
            : exeName);
}
