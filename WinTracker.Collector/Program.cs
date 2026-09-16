using System.Runtime.InteropServices;
using System.Threading;

if (args.Length == 1 && string.Equals(args[0], "--stop", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = CollectorControl.RequestStop() ? 0 : 1;
    return;
}

(string appRootPath, string settingsPath) = AppStorage.Resolve();
CollectorSettings settings = CollectorSettingsLoader.Load(settingsPath);
bool demoMode = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
args = args.Where(arg => !string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase)).ToArray();
if (demoMode)
{
    if (args.Length == 0 || (args[0] != "seed" && args[0] != "report"))
        throw new ArgumentException("--demo is supported for seed/report, not live collection.");
    settings = settings with { SqliteFilePath = AppStorage.DatabasePath(appRootPath, settings, demo: true) };
}
bool runBackground = args.Any(x => string.Equals(x, "--background", StringComparison.OrdinalIgnoreCase));

if (runBackground)
{
    HideConsoleWindow();
}

if (DummySeedConsole.TryHandle(args, settings, appRootPath, demoMode))
{
    return;
}

if (UsageReportConsole.TryHandle(args, settings, appRootPath, demoMode))
{
    return;
}

if (args.Any(arg => !string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Usage: WinTracker.Collector [--background|--stop] | seed/report [24h|1week] [--demo]");
    Environment.ExitCode = 2;
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

string sqlitePath = Path.Combine(appRootPath, settings.SqliteFilePath);
using var eventWriter = new SqliteEventWriter(sqlitePath, settings.StoreWindowTitles);
Console.WriteLine($"Logging to SQLite: {sqlitePath}");

using var cts = new CancellationTokenSource();
int shutdownRequested = 0;
using var shutdownCompleted = new ManualResetEventSlim(false);
using var stopSignal = new EventWaitHandle(false, EventResetMode.AutoReset, CollectorControl.StopEventName);
RegisteredWaitHandle stopRegistration = ThreadPool.RegisterWaitForSingleObject(
    stopSignal, (_, _) => RequestShutdown(), null, Timeout.Infinite, executeOnlyOnce: true);
ConsoleCtrlHandler shutdownHandler = OnConsoleControlSignal;

Console.CancelKeyPress += OnCancelKeyPress;
AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
_ = NativeMethods.SetConsoleCtrlHandler(shutdownHandler, add: true);

try
{
    await ForegroundCollector.RunEventDrivenAsync(
        cts.Token,
        eventWriter,
        settings);
}
finally
{
    try
    {
        eventWriter.Dispose();
    }
    finally
    {
        shutdownCompleted.Set();
        stopRegistration.Unregister(null);
        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _ = NativeMethods.SetConsoleCtrlHandler(shutdownHandler, add: false);
    }
}

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    RequestShutdown();
}

void OnProcessExit(object? sender, EventArgs e)
{
    RequestShutdown();
    shutdownCompleted.Wait(TimeSpan.FromSeconds(3));
}

bool OnConsoleControlSignal(int controlType)
{
    if (controlType is NativeMethods.CTRL_CLOSE_EVENT
        or NativeMethods.CTRL_LOGOFF_EVENT
        or NativeMethods.CTRL_SHUTDOWN_EVENT)
    {
        RequestShutdown();
        // Returning immediately allows Windows to terminate before the async loop flushes.
        shutdownCompleted.Wait(TimeSpan.FromSeconds(3));
        return true;
    }

    return false;
}

void RequestShutdown()
{
    if (Interlocked.CompareExchange(ref shutdownRequested, 1, 0) != 0)
    {
        return;
    }

    if (!cts.IsCancellationRequested)
    {
        cts.Cancel();
    }
}

static void HideConsoleWindow()
{
    IntPtr hwnd = NativeMethods.GetConsoleWindow();
    if (hwnd == IntPtr.Zero)
    {
        return;
    }

    _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
}

delegate bool ConsoleCtrlHandler(int controlType);

static class NativeMethods
{
    internal const int CTRL_CLOSE_EVENT = 2;
    internal const int CTRL_LOGOFF_EVENT = 5;
    internal const int CTRL_SHUTDOWN_EVENT = 6;
    internal const int SW_HIDE = 0;

    [DllImport("Kernel32", SetLastError = true)]
    internal static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handlerRoutine, bool add);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
