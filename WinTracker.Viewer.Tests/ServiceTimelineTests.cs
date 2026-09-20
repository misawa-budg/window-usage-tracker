using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class ServiceTimelineTests
{
    [Theory]
    [InlineData("host:keio.jp", true)]
    [InlineData("host:portal.keio.jp", true)]
    [InlineData("host:xn--r8jz45g.example", true)]
    [InlineData("host:localhost", false)]
    [InlineData("host:127.0.0.1", false)]
    [InlineData("host:[::1]", false)]
    [InlineData("host:UPPER.example", false)]
    [InlineData("host:example.com.", false)]
    [InlineData("host:-bad.example", false)]
    [InlineData("host:bad-.example", false)]
    [InlineData("host:a..example", false)]
    [InlineData("host:user@example.com", false)]
    [InlineData("host:example.com:443", false)]
    [InlineData("host:example.com/private?token=1", false)]
    [InlineData("host:example.com\n", false)]
    [InlineData("host:日本語.example", false)]
    [InlineData(null, false)]
    public void HostIdentifiersAreBoundedCanonicalDnsNames(string? id, bool expected) =>
        Assert.Equal(expected, BrowserServices.IsHostId(id));

    [Fact]
    public void HostRowsPreserveSubdomainsAndTotalsWithoutNeedingLabelConfiguration()
    {
        var start = DateTimeOffset.UnixEpoch;
        string[] services = ["gmail", "gemini", "host:drive.google.com", "host:keio.jp", "host:portal.keio.jp"];
        var projected = BrowserServices.ProjectForeground(services.Select((id, i) =>
            new AppStateIntervalRow("msedge.exe", "Active", start.AddMinutes(i), start.AddMinutes(i + 1), id)).ToArray());
        Assert.Equal(5, projected.Select(x => x.ExeName).Distinct().Count());
        Assert.Equal(300, projected.Sum(x => (x.StateEndUtc - x.StateStartUtc).TotalSeconds));
        Assert.Equal(new[] { "Gmail", "Gemini", "drive.google.com", "keio.jp", "portal.keio.jp" },
            projected.Select(x => AppChoice.FormatDisplayName(x.ExeName)));
        Assert.False(BrowserServices.IsHostId("host:" + new string('a', 64) + ".example"));
        Assert.False(BrowserServices.IsHostId("host:" + string.Join('.', Enumerable.Repeat(new string('a', 63), 4))));
    }

    [Fact]
    public void TwoBrowsersShareOneServiceRowWithoutAddingTheirParentTotals()
    {
        var start = DateTimeOffset.Parse("2026-09-20T00:00:00Z");
        var window = new UsageQueryWindow(start, start.AddHours(1), TimeSpan.FromMinutes(5));
        AppStateIntervalRow[] input = [
            new("msedge.exe", "Active", start, start.AddMinutes(10), "youtube"),
            new("chrome.exe", "Active", start.AddMinutes(10), start.AddMinutes(20), "youtube"),
            new("msedge.exe", "Active", start.AddMinutes(20), start.AddMinutes(30), "gmail"),
            new("msedge.exe", "Open", start.AddMinutes(30), start.AddHours(1))];
        var projected = BrowserServices.ProjectForeground(input);
        var builder = new TimelineLayoutBuilder();
        var rows = builder.BuildDailyAppRowsFromIntervals(projected, window, 960);
        Assert.Equal(2, rows.Count);
        var youtube = Assert.Single(rows, row => row.Label == "service:youtube");
        Assert.Equal("00:20", youtube.TotalLabel);
        Assert.StartsWith("YouTube | Active |", Assert.Single(youtube.Segments, x => !x.IsNoData).Tooltip);
        var overview = builder.BuildDailyStateStackRowsFromIntervals(projected.Select(x =>
            new ActiveIntervalRow(x.ExeName, x.StateStartUtc, x.StateEndUtc)).ToArray(), window, 960);
        Assert.Equal("00:30", Assert.Single(overview).TotalLabel);
    }
}
