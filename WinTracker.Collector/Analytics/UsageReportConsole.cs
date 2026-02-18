internal static class UsageReportConsole
{
    public static bool TryHandle(string[] args, CollectorSettings settings, string baseDirectory)
    {
        if (args.Length == 0)
        {
            return false;
        }

        if (!string.Equals(args[0], "report", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        UsageQueryWindow window = args.Length >= 2 && string.Equals(args[1], "1week", StringComparison.OrdinalIgnoreCase)
            ? UsageQueryWindow.Last7Days(nowUtc)
            : UsageQueryWindow.Last24Hours(nowUtc);

        if (args.Length >= 2 &&
            !string.Equals(args[1], "24h", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args[1], "1week", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: dotnet run --project .\\WinTracker\\WinTracker.csproj -- report [24h|1week]");
            return true;
        }

        string sqlitePath = Path.Combine(baseDirectory, settings.SqliteFilePath);
        if (!File.Exists(sqlitePath))
        {
            Console.WriteLine($"Database not found: {sqlitePath}");
            return true;
        }

        using var query = new SqliteUsageQueryService(sqlitePath);
        IReadOnlyList<AppUsageSummaryRow> summaries = query.QueryAppSummaries(window);
        IReadOnlyList<AppStateUsageRow> states = query.QueryStateTotals(window);
        IReadOnlyList<TimelineUsageRow> timeline = query.QueryTimeline(window);

        Console.WriteLine($"Report window: {window.FromUtc:O} - {window.ToUtc:O}");
        Console.WriteLine();

        Console.WriteLine("[App Summary]");
        foreach (AppUsageSummaryRow row in summaries.Take(15))
        {
            Console.WriteLine(
                $"{row.ExeName,-28} total={ToDisplay(row.TotalSeconds),8} " +
                $"active={ToDisplay(row.ActiveSeconds),8} " +
                $"open={ToDisplay(row.OpenSeconds),8} " +
                $"min={ToDisplay(row.MinimizedSeconds),8}");
        }

        Console.WriteLine();
        Console.WriteLine("[App x State]");
        foreach (AppStateUsageRow row in states.Take(30))
        {
            Console.WriteLine($"{row.ExeName,-28} {row.State,-10} {ToDisplay(row.Seconds),8}");
        }

        Console.WriteLine();
        Console.WriteLine("[Timeline sample]");
        foreach (TimelineUsageRow row in timeline.Take(40))
        {
            Console.WriteLine($"{row.BucketStartUtc:yyyy-MM-dd HH:mm}Z  {row.ExeName,-24} {row.State,-10} {ToDisplay(row.Seconds),8}");
        }

        return true;
    }

    private static string ToDisplay(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
