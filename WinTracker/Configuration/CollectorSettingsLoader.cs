using System.Text;
using System.Text.Json;

internal static class CollectorSettingsLoader
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static CollectorSettings Load(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            Console.WriteLine($"Settings not found, using defaults: {settingsPath}");
            return new CollectorSettings();
        }

        try
        {
            string json = File.ReadAllText(settingsPath, Encoding.UTF8);
            CollectorSettings? parsed = JsonSerializer.Deserialize<CollectorSettings>(json, DeserializeOptions);
            if (parsed is null)
            {
                Console.WriteLine($"Settings file is empty/invalid, using defaults: {settingsPath}");
                return new CollectorSettings();
            }

            int rescanInterval = parsed.RescanIntervalSeconds > 0
                ? parsed.RescanIntervalSeconds
                : (parsed.PollingIntervalSeconds > 0 ? parsed.PollingIntervalSeconds : 300);
            string[] excludedExeNames = parsed.ExcludedExeNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (excludedExeNames.Length == 0)
            {
                excludedExeNames = new CollectorSettings().ExcludedExeNames;
            }

            string sqliteFilePath = string.IsNullOrWhiteSpace(parsed.SqliteFilePath)
                ? new CollectorSettings().SqliteFilePath
                : parsed.SqliteFilePath;

            return new CollectorSettings
            {
                PollingIntervalSeconds = parsed.PollingIntervalSeconds,
                RescanIntervalSeconds = rescanInterval,
                SqliteFilePath = sqliteFilePath,
                ExcludedExeNames = excludedExeNames
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings, using defaults: {ex.Message}");
            return new CollectorSettings();
        }
    }
}
