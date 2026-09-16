using System.Runtime.InteropServices;

internal static class SessionAvailability
{
    // UOI_IO is false while this desktop is not receiving input (lock/secure desktop).
    public static bool IsInputDesktop()
    {
        IntPtr desktop = GetThreadDesktop(Win32.GetCurrentThreadId());
        if (desktop != IntPtr.Zero && GetUserObjectInformation(desktop, 6, out int receivesInput, sizeof(int), out _))
            return receivesInput != 0;

        Console.Error.WriteLine($"Cannot query input desktop; collection paused. Win32={Marshal.GetLastWin32Error()}");
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index,
        out int information, int length, out int lengthNeeded);
}
