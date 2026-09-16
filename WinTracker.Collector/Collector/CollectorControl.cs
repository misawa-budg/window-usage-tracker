internal static class CollectorControl
{
    internal const string StopEventName = @"Local\WinTrackerCollector.Stop";

    public static bool RequestStop()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!EventWaitHandle.TryOpenExisting(StopEventName, out EventWaitHandle? signal))
        {
            Console.Error.WriteLine("No compatible collector is running. Older releases must be stopped separately.");
            return false;
        }
        using (signal) signal.Set();
        Console.WriteLine("Graceful stop requested. Waiting for the collector to exit...");
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (!Mutex.TryOpenExisting(@"Local\WinTrackerCollector", out Mutex? mutex))
            {
                Console.WriteLine("Collector stopped.");
                return true;
            }
            mutex.Dispose();
            Thread.Sleep(100);
        }
        Console.Error.WriteLine("Stop was requested, but exit was not confirmed within 10 seconds.");
        return false;
    }
}
