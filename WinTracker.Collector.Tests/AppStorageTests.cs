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
}
