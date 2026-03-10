using System.Runtime.InteropServices;
using System.Threading;

string settingsPath = ResolveSettingsPath();
string appRootPath = Path.GetDirectoryName(settingsPath) ?? Environment.CurrentDirectory;
CollectorSettings settings = CollectorSettingsLoader.Load(settingsPath);
bool runBackground = args.Any(x => string.Equals(x, "--background", StringComparison.OrdinalIgnoreCase));

if (runBackground)
{
    HideConsoleWindow();
}

if (DummySeedConsole.TryHandle(args, settings, appRootPath))
{
    return;
}

if (UsageReportConsole.TryHandle(args, settings, appRootPath))
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

string sqlitePath = Path.Combine(appRootPath, settings.SqliteFilePath);
using var eventWriter = new SqliteEventWriter(sqlitePath);
Console.WriteLine($"Logging to SQLite: {sqlitePath}");

using var cts = new CancellationTokenSource();
int shutdownRequested = 0;
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
    Console.CancelKeyPress -= OnCancelKeyPress;
    AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    _ = NativeMethods.SetConsoleCtrlHandler(shutdownHandler, add: false);
}

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    RequestShutdown();
}

void OnProcessExit(object? sender, EventArgs e) => RequestShutdown();

bool OnConsoleControlSignal(int controlType)
{
    if (controlType is NativeMethods.CTRL_CLOSE_EVENT
        or NativeMethods.CTRL_LOGOFF_EVENT
        or NativeMethods.CTRL_SHUTDOWN_EVENT)
    {
        RequestShutdown();
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

static string ResolveSettingsPath()
{
    string fileName = "collector.settings.json";
    string currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, fileName);
    if (File.Exists(currentDirectoryPath))
    {
        return currentDirectoryPath;
    }

    return Path.Combine(AppContext.BaseDirectory, fileName);
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
