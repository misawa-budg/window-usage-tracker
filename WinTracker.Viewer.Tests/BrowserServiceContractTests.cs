using System.Text.Json;
using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class BrowserServiceContractTests
{
    public static IEnumerable<object?[]> Cases()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "browser-services.json")));
        return json.RootElement.EnumerateArray().Select(x => new object?[] {
            x.GetProperty("url").GetString(), x.GetProperty("defaultId").GetString(),
            x.GetProperty("hostId").GetString(), x.GetProperty("displayName").GetString() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExtensionOutputsAreAcceptedAndDisplayedByTheCollectorContract(
        string? url, string? defaultId, string? hostId, string displayName)
    {
        _ = url; // Names the same case as the JavaScript classifier test; no URL enters storage.
        Assert.Equal(defaultId is not null, BrowserServices.IsAllowed(defaultId, false));
        Assert.Equal(hostId is not null, BrowserServices.IsAllowed(hostId, true));
        if (hostId != defaultId) Assert.False(BrowserServices.IsAllowed(hostId, false));
        var start = DateTimeOffset.UnixEpoch;
        var projected = Assert.Single(BrowserServices.ProjectForeground([
            new("msedge.exe", "Active", start, start.AddMinutes(1), hostId)]));
        Assert.Equal(displayName, AppChoice.FormatDisplayName(projected.ExeName));
    }
}
