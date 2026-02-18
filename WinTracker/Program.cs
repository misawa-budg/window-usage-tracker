using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Encodings.Web;

Console.WriteLine("Foreground polling started. Press Ctrl+C to stop.");
string logDirectoryPath = Path.Combine(Environment.CurrentDirectory, "logs");
using var eventWriter = new JsonlEventWriter(logDirectoryPath);
Console.WriteLine($"Logging to: {logDirectoryPath}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await ForegroundCollector.RunPollingAsync(TimeSpan.FromSeconds(1), cts.Token, eventWriter);

internal static class ForegroundCollector
{
    // 学習用の暫定除外リスト。環境に合わせて調整する。
    private static readonly HashSet<string> ExcludedExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm.exe",
        "TextInputHost.exe",
        "NVIDIA Overlay.exe",
        "Overwolf.exe",
        "ArmourySwAgent.exe"
    };

    public static async Task RunPollingAsync(TimeSpan interval, CancellationToken cancellationToken, JsonlEventWriter eventWriter)
    {
        var intervalsByApp = new Dictionary<string, AppInterval>(StringComparer.OrdinalIgnoreCase);
        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
            Dictionary<string, AppSnapshot> currentByApp = CaptureCurrentStates();

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
                    // 同じ状態は区間を延長し、保存はしない（重複抑制）。
                    intervalsByApp[appKey] = existing with
                    {
                        StateEndUtc = observedAtUtc,
                        Pid = current.Pid,
                        Hwnd = current.Hwnd,
                        Title = current.Title
                    };
                    continue;
                }

                // 状態遷移を検知したら、前区間を確定して保存。
                AppInterval closedInterval = existing with { StateEndUtc = observedAtUtc };
                WriteClosedInterval(eventWriter, closedInterval);

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
                WriteClosedInterval(eventWriter, closedInterval);
                intervalsByApp.Remove(removedKey);
            }

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // 停止時に未確定区間をflushする。
        DateTimeOffset stoppedAtUtc = DateTimeOffset.UtcNow;
        foreach (AppInterval intervalState in intervalsByApp.Values)
        {
            WriteClosedInterval(eventWriter, intervalState with { StateEndUtc = stoppedAtUtc });
        }
    }

    private static void WriteClosedInterval(JsonlEventWriter eventWriter, AppInterval interval)
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
            Source: "polling");

        eventWriter.Write(appEvent);
        Console.WriteLine(JsonSerializer.Serialize(appEvent));
    }

    private static Dictionary<string, AppSnapshot> CaptureCurrentStates()
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
            if (!ShouldTrackWindow(hwnd, exeName, title, minimized))
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

        MergeByPriority(byApp, active);
        return byApp;
    }

    private static bool ShouldTrackWindow(IntPtr hwnd, string exeName, string title, bool minimized)
    {
        if (ExcludedExeNames.Contains(exeName))
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

internal sealed class JsonlEventWriter : IDisposable
{
    private readonly string _directoryPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private StreamWriter? _writer;
    private string? _activeDateKey;

    public JsonlEventWriter(string directoryPath)
    {
        _directoryPath = directoryPath;
        Directory.CreateDirectory(_directoryPath);
    }

    public void Write(AppEvent appEvent)
    {
        string dateKey = appEvent.StateEndUtc.UtcDateTime.ToString("yyyyMMdd");
        EnsureWriter(dateKey);

        string line = JsonSerializer.Serialize(appEvent, _jsonOptions);
        _writer!.WriteLine(line);
        _writer.Flush();
    }

    public void Dispose() => _writer?.Dispose();

    private void EnsureWriter(string dateKey)
    {
        if (_writer is not null && string.Equals(_activeDateKey, dateKey, StringComparison.Ordinal))
        {
            return;
        }

        _writer?.Dispose();
        string filePath = Path.Combine(_directoryPath, $"events-{dateKey}.jsonl");
        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _activeDateKey = dateKey;
    }
}

internal static class Win32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

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
