using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class TimelineDisplayDataTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly AppStateIntervalRow[] Source = [
        new("msedge.exe", "Active", Start, Start.AddMinutes(10), "host:portal.example"),
        new("chrome.exe", "Active", Start.AddMinutes(10), Start.AddMinutes(20), "gmail"),
        new("Code.exe", "Open", Start, Start.AddMinutes(30)),
        new("Code.exe", "Minimized", Start.AddMinutes(30), Start.AddMinutes(40))];

    [Theory]
    [InlineData(AppDisplayMode.Services, 2)]
    [InlineData(AppDisplayMode.Running, 4)]
    [InlineData(AppDisplayMode.StateDetails, 4)]
    public void ModesPreserveForegroundTotalsAndSource(AppDisplayMode mode, int rows)
    {
        var before = Source.ToArray();
        var data = TimelineDisplayData.Create(Source, mode);
        Assert.Equal(rows, data.AppIntervals.Count);
        Assert.Equal(1200, data.ActiveIntervals.Sum(x => (x.StateEndUtc - x.StateStartUtc).TotalSeconds));
        Assert.Equal(before, Source);
        Assert.Same(data.AppIntervals, data.AppIntervals); // selectors/renderers reuse the snapshot
        if (mode == AppDisplayMode.Services)
            Assert.Equal(new[] { "service:host:portal.example", "service:gmail" }, data.AppIntervals.Select(x => x.ExeName));
        if (mode == AppDisplayMode.Running)
        {
            Assert.All(data.AppIntervals, x => { Assert.Equal("Running", x.State); Assert.Null(x.ServiceId); });
            Assert.Equal(new[] { "msedge.exe", "chrome.exe" }, data.ActiveIntervals.Select(x => x.ExeName));
        }
        if (mode == AppDisplayMode.StateDetails) Assert.Same(Source, data.AppIntervals);
    }

    [Theory]
    [InlineData(AppDisplayMode.Services)]
    [InlineData(AppDisplayMode.Running)]
    [InlineData(AppDisplayMode.StateDetails)]
    public void NewSnapshotsDoNotReusePreviousModesOrPreviousData(AppDisplayMode mode)
    {
        var first = TimelineDisplayData.Create(Source, AppDisplayMode.Services);
        var next = TimelineDisplayData.Create([], mode);
        Assert.Empty(next.AppIntervals);
        Assert.Empty(next.ActiveIntervals);
        Assert.Equal(2, first.AppIntervals.Count);
    }
}
