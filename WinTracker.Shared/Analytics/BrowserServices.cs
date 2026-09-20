namespace WinTracker.Shared.Analytics;

public static class BrowserServices
{
    public static bool IsKnown(string? id) => id is "youtube" or "twitch" or "gmail" or "github" or
        "netflix" or "unext" or "chatgpt" or "claude" or "gemini" or "other-web";

    // Exact hosts, not registrable domains: mail.google.com and gemini.google.com must stay separate.
    public static bool IsHostId(string? id)
    {
        if (id is null || !id.StartsWith("host:", StringComparison.Ordinal)) return false;
        string host = id[5..];
        if (host.Length is < 3 or > 253 || !host.Contains('.') ||
            System.Net.IPAddress.TryParse(host, out _)) return false;
        return host.Split('.').All(label => label.Length is >= 1 and <= 63 &&
            label[0] != '-' && label[^1] != '-' &&
            label.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'));
    }

    public static bool IsAllowed(string? id, bool storeBrowserHostnames) =>
        IsKnown(id) || (storeBrowserHostnames && IsHostId(id));

    public static bool IsBrowser(string exe) =>
        exe.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) ||
        exe.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase);

    public static string? DisplayName(string key) => key switch
    {
        "service:youtube" => "YouTube",
        "service:twitch" => "Twitch",
        "service:gmail" => "Gmail",
        "service:github" => "GitHub",
        "service:netflix" => "Netflix",
        "service:unext" => "U-NEXT",
        "service:chatgpt" => "ChatGPT（Web）",
        "service:claude" => "Claude（Web）",
        "service:gemini" => "Gemini",
        "service:other-web" => "その他のWeb",
        "browser:edge:unknown" => "Edge（未取得）",
        "browser:chrome:unknown" => "Chrome（未取得）",
        _ => key.StartsWith("service:", StringComparison.Ordinal) && IsHostId(key[8..]) ? key[13..] : null
    };

    // Replace browser foreground intervals; never append services to app totals.
    // Open/Minimized remain available only in the original app-state view.
    public static IReadOnlyList<AppStateIntervalRow> ProjectForeground(IReadOnlyList<AppStateIntervalRow> rows) =>
        rows.Where(row => row.State == "Active").Select(row =>
        {
            if (!IsBrowser(row.ExeName)) return row;
            // Historical opt-in records remain readable after recording is disabled.
            string key = IsAllowed(row.ServiceId, storeBrowserHostnames: true) ? "service:" + row.ServiceId :
                row.ExeName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)
                    ? "browser:edge:unknown" : "browser:chrome:unknown";
            return row with { ExeName = key };
        }).ToArray();
}
