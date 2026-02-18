using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Text.Json;

Console.WriteLine("Foreground polling started. Press Ctrl+C to stop.");
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await ForegroundCollector.RunPollingAsync(TimeSpan.FromSeconds(1), cts.Token);

internal static class ForegroundCollector
{
    public static async Task RunPollingAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        string? lastKey = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            AppEvent? appEvent = TryCaptureActive();
            if (appEvent is AppEvent current)
            {
                string key = $"{current.ExeName}|{current.Pid}|{current.Hwnd}|{current.State}";
                if (!string.Equals(lastKey, key, StringComparison.Ordinal))
                {
                    Console.WriteLine(JsonSerializer.Serialize(current));
                    lastKey = key;
                }
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
    }

    private static AppEvent? TryCaptureActive()
    {
        IntPtr hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine($"GetForegroundWindow failed: {Marshal.GetLastWin32Error()}");
            return null;
        }

        uint threadId = Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (threadId == 0 || pid == 0)
        {
            Console.WriteLine($"GetWindowThreadProcessId failed: {Marshal.GetLastWin32Error()}");
            return null;
        }

        string title = GetWindowTitle(hwnd);
        string exeName = GetExeName(pid);

        return new AppEvent(
            EventAtUtc: DateTimeOffset.UtcNow,
            ExeName: exeName,
            Pid: pid,
            Hwnd: ToHexHwnd(hwnd),
            Title: title,
            State: "Active");
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
    DateTimeOffset EventAtUtc,
    string ExeName,
    uint Pid,
    string Hwnd,
    string Title,
    string State);

internal static class Win32
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);
}
