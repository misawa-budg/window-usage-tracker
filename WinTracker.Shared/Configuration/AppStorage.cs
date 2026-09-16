namespace WinTracker.Shared.Configuration;

public static class AppStorage
{
    public static (string Root, string SettingsPath) Resolve(string? workingDirectory = null,
        string? applicationDirectory = null, string? explicitRoot = null)
    {
        explicitRoot ??= Environment.GetEnvironmentVariable("WINTRACKER_HOME");
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return AtRoot(Path.GetFullPath(explicitRoot));

        // Portable launches use their bundle root. Development launches share the solution root,
        // whether started by dotnet run, Visual Studio, or directly from bin/.
        foreach (string origin in new[] { workingDirectory ?? Environment.CurrentDirectory, applicationDirectory ?? AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(origin); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "WinTracker.slnx")))
                    return (directory.FullName, Path.Combine(directory.FullName, "WinTracker.Collector", "collector.settings.json"));
                if (File.Exists(Path.Combine(directory.FullName, "collector.settings.json")))
                {
                    if (directory.Parent is { } parent && File.Exists(Path.Combine(parent.FullName, "WinTracker.slnx")))
                        return (parent.FullName, Path.Combine(directory.FullName, "collector.settings.json"));
                    return AtRoot(directory.FullName);
                }
            }
        }
        return AtRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTracker"));
    }

    public static string DatabasePath(string root, CollectorSettings settings, bool demo = false)
    {
        string real = Path.GetFullPath(Path.Combine(root, settings.SqliteFilePath));
        if (!demo) return real;
        string demoPath = Path.GetFullPath(Path.Combine(root, "data", "demo.db"));
        if (string.Equals(real, demoPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The real database must not use the reserved demo database path.");
        return demoPath;
    }

    private static (string Root, string SettingsPath) AtRoot(string root) =>
        (root, Path.Combine(root, "collector.settings.json"));
}
