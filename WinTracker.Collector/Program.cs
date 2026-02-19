string settingsPath = Path.Combine(Environment.CurrentDirectory, "collector.settings.json");
CollectorSettings settings = CollectorSettingsLoader.Load(settingsPath);

if (DummySeedConsole.TryHandle(args, settings, Environment.CurrentDirectory))
{
    return;
}

if (UsageReportConsole.TryHandle(args, settings, Environment.CurrentDirectory))
{
    return;
}

using var singleInstanceMutex = new Mutex(
    initiallyOwned: true,
    name: @"Local\WinTrackerCollector",
    createdNew: out bool isFirstInstance);

if (!isFirstInstance)
{
    Console.WriteLine("Collector is already running. Exit.");
    return;
}

Console.WriteLine("Event-driven collector started. Press Ctrl+C to stop.");
Console.WriteLine($"Rescan interval: {settings.RescanIntervalSeconds}s");

string sqlitePath = Path.Combine(Environment.CurrentDirectory, settings.SqliteFilePath);
using var eventWriter = new SqliteEventWriter(sqlitePath);
Console.WriteLine($"Logging to SQLite: {sqlitePath}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += OnCancelKeyPress;

await ForegroundCollector.RunEventDrivenAsync(
    cts.Token,
    eventWriter,
    settings);

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    cts.Cancel();
}
