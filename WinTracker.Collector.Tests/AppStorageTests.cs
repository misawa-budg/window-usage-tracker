using Xunit;

namespace WinTracker.Collector.Tests;

public sealed class AppStorageTests
{
    [Fact]
    public void DevelopmentLaunchesResolveTheSameRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"wintracker-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "WinTracker.Collector", "bin"));
        Directory.CreateDirectory(Path.Combine(root, "WinTracker.Viewer", "bin"));
        try
        {
            File.WriteAllText(Path.Combine(root, "WinTracker.slnx"), "");
            File.WriteAllText(Path.Combine(root, "WinTracker.Collector", "collector.settings.json"), "{}");
            var collector = AppStorage.Resolve(Path.Combine(root, "WinTracker.Collector", "bin"), root, "");
            var viewer = AppStorage.Resolve(Path.Combine(root, "WinTracker.Viewer", "bin"), root, "");
            Assert.Equal(collector, viewer);
            Assert.Equal(root, collector.Root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CustomDatabaseAndDemoDatabaseAreDistinct()
    {
        string root = Path.GetTempPath();
        var settings = new CollectorSettings { SqliteFilePath = "custom/usage.db" };
        Assert.Equal(Path.Combine(root, "custom", "usage.db"), AppStorage.DatabasePath(root, settings));
        Assert.Equal(Path.Combine(root, "data", "demo.db"), AppStorage.DatabasePath(root, settings, demo: true));
        Assert.Throws<InvalidOperationException>(() => AppStorage.DatabasePath(root,
            settings with { SqliteFilePath = "data/demo.db" }, demo: true));
    }

    [Fact]
    public void DirectPortableExeUsesBundleSettingsNotNestedSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), $"wintracker-portable-{Guid.NewGuid():N}");
        string collector = Path.Combine(root, "collector");
        Directory.CreateDirectory(collector);
        try
        {
            File.WriteAllText(Path.Combine(root, "Run-Collector.cmd"), "");
            File.WriteAllText(Path.Combine(root, "collector.settings.json"), "{}");
            File.WriteAllText(Path.Combine(collector, "collector.settings.json"), "{}");
            Assert.Equal(root, AppStorage.Resolve(collector, collector, "").Root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SeedWithoutDemoRefusesToCreateDatabase()
    {
        string root = Path.Combine(Path.GetTempPath(), $"wintracker-seed-{Guid.NewGuid():N}");
        Assert.True(DummySeedConsole.TryHandle(["seed", "--replace-all"], new CollectorSettings(), root));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void DemoActiveIntervalsNeverOverlapAndRunningTimeIsPreserved()
    {
        var start = DateTimeOffset.UnixEpoch;
        var events = DummySeedConsole.NormalizeActiveIntervals(new[]
        {
            new AppEvent(start, start.AddSeconds(10), "a.exe", 1, "", "", "Active", "demo-seed"),
            new AppEvent(start.AddSeconds(5), start.AddSeconds(15), "b.exe", 2, "", "", "Active", "demo-seed")
        });
        var active = events.Where(x => x.State == "Active").ToArray();
        Assert.Equal(active[0].StateEndUtc, active[1].StateStartUtc);
        Assert.Equal(20, events.Sum(x => (x.StateEndUtc - x.StateStartUtc).TotalSeconds));
        Assert.Contains(events, x => x.ExeName == "b.exe" && x.State == "Open");
    }
}
