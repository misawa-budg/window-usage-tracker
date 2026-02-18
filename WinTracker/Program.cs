using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;

string settingsPath = Path.Combine(Environment.CurrentDirectory, "collector.settings.json");
CollectorSettings settings = CollectorSettingsLoader.Load(settingsPath);

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
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await ForegroundCollector.RunEventDrivenAsync(
    cts.Token,
    eventWriter,
    settings);

internal static class ForegroundCollector
{
    public static async Task RunEventDrivenAsync(
        CancellationToken cancellationToken,
        IAppEventWriter eventWriter,
        CollectorSettings settings)
    {
        var excludedExeNames = new HashSet<string>(settings.ExcludedExeNames, StringComparer.OrdinalIgnoreCase);
        var intervalsByApp = new Dictionary<string, AppInterval>(StringComparer.OrdinalIgnoreCase);
        var signals = Channel.CreateUnbounded<CollectReason>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        using var hookPump = new WinEventHookPump(reason => _ = signals.Writer.TryWrite(reason));
        hookPump.Start();
        _ = signals.Writer.TryWrite(CollectReason.Startup);

        using var rescanTimer = new PeriodicTimer(TimeSpan.FromSeconds(settings.RescanIntervalSeconds));
        Task rescanTask = Task.Run(async () =>
        {
            try
            {
                while (await rescanTimer.WaitForNextTickAsync(cancellationToken))
                {
                    _ = signals.Writer.TryWrite(CollectReason.Rescan);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CollectReason reason;
                try
                {
                    reason = await signals.Reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // 連続イベントはまとめて1回のスナップショット評価にする。
                while (signals.Reader.TryRead(out CollectReason nextReason))
                {
                    reason = nextReason;
                }

                DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
                Dictionary<string, AppSnapshot> currentByApp = CaptureCurrentStates(excludedExeNames);
                string source = reason == CollectReason.Rescan ? "rescan" : "win_event";
                ApplySnapshot(currentByApp, observedAtUtc, source, intervalsByApp, eventWriter);
            }
        }
        finally
        {
            // 停止時に未確定区間をflushする。
            DateTimeOffset stoppedAtUtc = DateTimeOffset.UtcNow;
            foreach (AppInterval intervalState in intervalsByApp.Values)
            {
                WriteClosedInterval(eventWriter, intervalState with { StateEndUtc = stoppedAtUtc }, "shutdown");
            }

            try
            {
                await rescanTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static void ApplySnapshot(
        Dictionary<string, AppSnapshot> currentByApp,
        DateTimeOffset observedAtUtc,
        string source,
        Dictionary<string, AppInterval> intervalsByApp,
        IAppEventWriter eventWriter)
    {
        foreach ((string appKey, AppSnapshot current) in currentByApp)
        {
            if (!intervalsByApp.TryGetValue(appKey, out AppInterval existing))
            {
                intervalsByApp[appKey] = new AppInterval(
                    StateStartUtc: observedAtUtc,
                    StateEndUtc: observedAtUtc,
                    ExeName: current.ExeName,
                    Pid: current.Pid,
                    Hwnd: current.Hwnd,
                    Title: current.Title,
                    State: current.State);
                continue;
            }

            if (string.Equals(existing.State, current.State, StringComparison.Ordinal))
            {
                intervalsByApp[appKey] = existing with
                {
                    StateEndUtc = observedAtUtc,
                    Pid = current.Pid,
                    Hwnd = current.Hwnd,
                    Title = current.Title
                };
                continue;
            }

            AppInterval closedInterval = existing with { StateEndUtc = observedAtUtc };
            WriteClosedInterval(eventWriter, closedInterval, source);

            intervalsByApp[appKey] = new AppInterval(
                StateStartUtc: observedAtUtc,
                StateEndUtc: observedAtUtc,
                ExeName: current.ExeName,
                Pid: current.Pid,
                Hwnd: current.Hwnd,
                Title: current.Title,
                State: current.State);
        }

        foreach (string removedKey in intervalsByApp.Keys.Except(currentByApp.Keys).ToList())
        {
            AppInterval closedInterval = intervalsByApp[removedKey] with { StateEndUtc = observedAtUtc };
            WriteClosedInterval(eventWriter, closedInterval, source);
            intervalsByApp.Remove(removedKey);
        }
    }

    private static void WriteClosedInterval(IAppEventWriter eventWriter, AppInterval interval, string source)
    {
        if (interval.StateEndUtc < interval.StateStartUtc)
        {
            return;
        }

        var appEvent = new AppEvent(
            StateStartUtc: interval.StateStartUtc,
            StateEndUtc: interval.StateEndUtc,
            ExeName: interval.ExeName,
            Pid: interval.Pid,
            Hwnd: interval.Hwnd,
            Title: interval.Title,
            State: interval.State,
            Source: source);

        eventWriter.Write(appEvent);
        Console.WriteLine(JsonSerializer.Serialize(appEvent));
    }

    private static Dictionary<string, AppSnapshot> CaptureCurrentStates(HashSet<string> excludedExeNames)
    {
        var byApp = new Dictionary<string, AppSnapshot>(StringComparer.OrdinalIgnoreCase);
        var exeNameCache = new Dictionary<uint, string>();

        IntPtr shellWindow = Win32.GetShellWindow();
        _ = Win32.EnumWindows((hwnd, _) =>
        {
            if (hwnd == IntPtr.Zero || hwnd == shellWindow)
            {
                return true;
            }

            bool minimized = Win32.IsIconic(hwnd);
            bool visible = Win32.IsWindowVisible(hwnd);
            if (!visible && !minimized)
            {
                return true;
            }

            uint threadId = Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (threadId == 0 || pid == 0)
            {
                return true;
            }

            string exeName = GetExeName(pid, exeNameCache);
            string title = GetWindowTitle(hwnd);
            string state = minimized ? "Minimized" : "Open";
            if (!ShouldTrackWindow(hwnd, exeName, title, minimized, excludedExeNames))
            {
                return true;
            }

            var candidate = new AppSnapshot(
                ExeName: exeName,
                Pid: pid,
                Hwnd: ToHexHwnd(hwnd),
                Title: title,
                State: state);

            MergeByPriority(byApp, candidate);
            return true;
        }, IntPtr.Zero);

        IntPtr fgHwnd = Win32.GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero)
        {
            return byApp;
        }

        uint fgThreadId = Win32.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
        if (fgThreadId == 0 || fgPid == 0)
        {
            Console.WriteLine($"GetWindowThreadProcessId failed: {Marshal.GetLastWin32Error()}");
            return byApp;
        }

        var active = new AppSnapshot(
            ExeName: GetExeName(fgPid, exeNameCache),
            Pid: fgPid,
            Hwnd: ToHexHwnd(fgHwnd),
            Title: GetWindowTitle(fgHwnd),
            State: "Active");

        if (!ShouldTrackWindow(fgHwnd, active.ExeName, active.Title, minimized: false, excludedExeNames))
        {
            return byApp;
        }

        MergeByPriority(byApp, active);
        return byApp;
    }

    private static bool ShouldTrackWindow(
        IntPtr hwnd,
        string exeName,
        string title,
        bool minimized,
        HashSet<string> excludedExeNames)
    {
        if (excludedExeNames.Contains(exeName))
        {
            return false;
        }

        if (Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        if (!minimized && string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (!minimized && Win32.IsWindowCloaked(hwnd))
        {
            return false;
        }

        if (!minimized && Win32.TryGetWindowRect(hwnd, out Win32.RECT rect))
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 50 || height < 50)
            {
                return false;
            }
        }

        return true;
    }

    private static void MergeByPriority(Dictionary<string, AppSnapshot> byApp, AppSnapshot candidate)
    {
        string key = candidate.ExeName;
        if (!byApp.TryGetValue(key, out AppSnapshot existing))
        {
            byApp[key] = candidate;
            return;
        }

        int candidatePriority = GetStatePriority(candidate.State);
        int existingPriority = GetStatePriority(existing.State);
        if (candidatePriority > existingPriority)
        {
            byApp[key] = candidate;
            return;
        }

        if (candidatePriority == existingPriority &&
            string.IsNullOrEmpty(existing.Title) &&
            !string.IsNullOrEmpty(candidate.Title))
        {
            byApp[key] = candidate;
        }
    }

    private static int GetStatePriority(string state) =>
        state switch
        {
            "Active" => 3,
            "Minimized" => 2,
            "Open" => 1,
            _ => 0
        };

    private static string GetExeName(uint pid, Dictionary<uint, string> cache)
    {
        if (cache.TryGetValue(pid, out string? cached))
        {
            return cached;
        }

        string exeName = GetExeName(pid);
        cache[pid] = exeName;
        return exeName;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        int len = Win32.GetWindowTextLength(hwnd);
        if (len <= 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(len + 1);
        _ = Win32.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetExeName(uint pid)
    {
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return "unknown.exe";
        }
    }

    private static string ToHexHwnd(IntPtr hwnd)
    {
        ulong value = unchecked((ulong)hwnd.ToInt64());
        return $"0x{value:X}";
    }
}

internal readonly record struct AppEvent(
    DateTimeOffset StateStartUtc,
    DateTimeOffset StateEndUtc,
    string ExeName,
    uint Pid,
    string Hwnd,
    string Title,
    string State,
    string Source);

internal readonly record struct AppInterval(
    DateTimeOffset StateStartUtc,
    DateTimeOffset StateEndUtc,
    string ExeName,
    uint Pid,
    string Hwnd,
    string Title,
    string State);

internal readonly record struct AppSnapshot(
    string ExeName,
    uint Pid,
    string Hwnd,
    string Title,
    string State);

internal enum CollectReason
{
    Startup,
    WinEvent,
    Rescan
}

internal sealed class WinEventHookPump : IDisposable
{
    private readonly Action<CollectReason> _onEvent;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly List<IntPtr> _hookHandles = [];
    private Thread? _thread;
    private uint _threadId;
    private Exception? _startException;
    private Win32.WinEventProc? _callback;

    public WinEventHookPump(Action<CollectReason> onEvent)
    {
        _onEvent = onEvent;
    }

    public void Start()
    {
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "WinEventHookPump"
        };
        _thread.Start();
        _started.Wait();

        if (_startException is not null)
        {
            throw new InvalidOperationException("Failed to start WinEvent hooks.", _startException);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            _ = Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(3000);
        _started.Dispose();
    }

    private void ThreadMain()
    {
        try
        {
            _threadId = Win32.GetCurrentThreadId();
            _ = Win32.PeekMessage(out _, IntPtr.Zero, 0, 0, Win32.PM_NOREMOVE);

            _callback = HandleWinEvent;
            RegisterHook(Win32.EVENT_SYSTEM_FOREGROUND);
            RegisterHook(Win32.EVENT_SYSTEM_MINIMIZESTART);
            RegisterHook(Win32.EVENT_SYSTEM_MINIMIZEEND);

            _started.Set();

            while (Win32.GetMessage(out Win32.MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                _ = Win32.TranslateMessage(ref msg);
                _ = Win32.DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _startException = ex;
            _started.Set();
        }
        finally
        {
            foreach (IntPtr hookHandle in _hookHandles)
            {
                _ = Win32.UnhookWinEvent(hookHandle);
            }
        }
    }

    private void RegisterHook(uint eventType)
    {
        IntPtr hookHandle = Win32.SetWinEventHook(
            eventType,
            eventType,
            IntPtr.Zero,
            _callback!,
            0,
            0,
            Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);

        if (hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SetWinEventHook failed for 0x{eventType:X}: {Marshal.GetLastWin32Error()}");
        }

        _hookHandles.Add(hookHandle);
    }

    private void HandleWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != Win32.OBJID_WINDOW || idChild != 0)
        {
            return;
        }

        _onEvent(CollectReason.WinEvent);
    }
}

internal sealed class CollectorSettings
{
    // 互換性のために残す（旧設定名）。
    public int PollingIntervalSeconds { get; init; } = 0;
    public int RescanIntervalSeconds { get; init; } = 300;
    public string SqliteFilePath { get; init; } = Path.Combine("data", "collector.db");

    public string[] ExcludedExeNames { get; init; } =
    [
        "dwm.exe",
        "TextInputHost.exe",
        "NVIDIA Overlay.exe",
        "Overwolf.exe",
        "ArmourySwAgent.exe"
    ];
}

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

internal interface IAppEventWriter : IDisposable
{
    void Write(AppEvent appEvent);
}

internal sealed class SqliteEventWriter : IAppEventWriter
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _insertCommand;

    public SqliteEventWriter(string databasePath)
    {
        string? directoryPath = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        _connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared");
        _connection.Open();
        InitializeSchema(_connection);

        _insertCommand = _connection.CreateCommand();
        _insertCommand.CommandText =
            """
            INSERT INTO app_events (
                event_at_utc,
                state_start_utc,
                state_end_utc,
                exe_name,
                pid,
                hwnd,
                title,
                state,
                source
            ) VALUES (
                $event_at_utc,
                $state_start_utc,
                $state_end_utc,
                $exe_name,
                $pid,
                $hwnd,
                $title,
                $state,
                $source
            );
            """;
        _insertCommand.Parameters.Add("$event_at_utc", SqliteType.Text);
        _insertCommand.Parameters.Add("$state_start_utc", SqliteType.Text);
        _insertCommand.Parameters.Add("$state_end_utc", SqliteType.Text);
        _insertCommand.Parameters.Add("$exe_name", SqliteType.Text);
        _insertCommand.Parameters.Add("$pid", SqliteType.Integer);
        _insertCommand.Parameters.Add("$hwnd", SqliteType.Text);
        _insertCommand.Parameters.Add("$title", SqliteType.Text);
        _insertCommand.Parameters.Add("$state", SqliteType.Text);
        _insertCommand.Parameters.Add("$source", SqliteType.Text);
    }

    public void Write(AppEvent appEvent)
    {
        _insertCommand.Parameters["$event_at_utc"].Value = appEvent.StateEndUtc.ToString("O");
        _insertCommand.Parameters["$state_start_utc"].Value = appEvent.StateStartUtc.ToString("O");
        _insertCommand.Parameters["$state_end_utc"].Value = appEvent.StateEndUtc.ToString("O");
        _insertCommand.Parameters["$exe_name"].Value = appEvent.ExeName;
        _insertCommand.Parameters["$pid"].Value = (long)appEvent.Pid;
        _insertCommand.Parameters["$hwnd"].Value = appEvent.Hwnd;
        _insertCommand.Parameters["$title"].Value = appEvent.Title;
        _insertCommand.Parameters["$state"].Value = appEvent.State;
        _insertCommand.Parameters["$source"].Value = appEvent.Source;
        _insertCommand.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _insertCommand.Dispose();
        _connection.Dispose();
    }

    private static void InitializeSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS app_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_at_utc TEXT NOT NULL,
                state_start_utc TEXT NOT NULL,
                state_end_utc TEXT NOT NULL,
                exe_name TEXT NOT NULL,
                pid INTEGER NOT NULL,
                hwnd TEXT NOT NULL,
                title TEXT NOT NULL,
                state TEXT NOT NULL,
                source TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_app_events_time
            ON app_events(event_at_utc);

            CREATE INDEX IF NOT EXISTS idx_app_events_exe_time
            ON app_events(exe_name, event_at_utc);
            """;
        command.ExecuteNonQuery();
    }
}

internal static class Win32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint WM_QUIT = 0x0012;
    public const uint PM_NOREMOVE = 0x0000;
    public const int OBJID_WINDOW = 0;
    public const uint GW_OWNER = 4;
    private const uint DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public static bool IsWindowCloaked(IntPtr hWnd)
    {
        int cloaked = 0;
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
        if (hr != 0)
        {
            return false;
        }

        return cloaked != 0;
    }

    public static bool TryGetWindowRect(IntPtr hWnd, out RECT rect) => GetWindowRect(hWnd, out rect);
}
