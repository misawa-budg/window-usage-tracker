using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class WindowSnapshotProvider
{
    private const int MinimumWindowWidth = 50;
    private const int MinimumWindowHeight = 50;

    public static Dictionary<string, AppSnapshot> CaptureCurrentStates(HashSet<string> excludedExeNames)
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
            if (width < MinimumWindowWidth || height < MinimumWindowHeight)
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
        if (len <= 0)
        {
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
