using System.Runtime.InteropServices;

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
