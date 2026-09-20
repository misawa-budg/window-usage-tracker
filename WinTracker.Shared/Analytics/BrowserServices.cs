namespace WinTracker.Shared.Analytics;

public static class BrowserServices
{
    public static bool IsKnown(string? id) => id is "youtube" or "twitch" or "gmail" or "github" or "other-web";

    public static bool IsBrowser(string exe) =>
        exe.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) ||
        exe.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase);

    public static string? DisplayName(string key) => key switch
    {
        "service:youtube" => "YouTube",
        "service:twitch" => "Twitch",
        "service:gmail" => "Gmail",
        "service:github" => "GitHub",
        "service:other-web" => "その他のWeb",
        "browser:edge:unknown" => "Edge（未取得）",
        "browser:chrome:unknown" => "Chrome（未取得）",
        _ => null
    };

    // Replace browser foreground intervals; never append services to app totals.
    // Open/Minimized remain available only in the original app-state view.
    public static IReadOnlyList<AppStateIntervalRow> ProjectForeground(IReadOnlyList<AppStateIntervalRow> rows) =>
        rows.Where(row => row.State == "Active").Select(row =>
        {
            if (!IsBrowser(row.ExeName)) return row;
            string key = IsKnown(row.ServiceId) ? "service:" + row.ServiceId :
                row.ExeName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)
                    ? "browser:edge:unknown" : "browser:chrome:unknown";
            return row with { ExeName = key };
        }).ToArray();
}
