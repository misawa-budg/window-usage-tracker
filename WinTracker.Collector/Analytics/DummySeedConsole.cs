using Microsoft.Data.Sqlite;

internal static class DummySeedConsole
{
    private const string SeedSource = "demo-seed";

    public static bool TryHandle(string[] args, CollectorSettings settings, string baseDirectory)
    {
        if (args.Length == 0 || !string.Equals(args[0], "seed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string range = "24h";
        bool replace = false;

        foreach (string arg in args.Skip(1))
        {
            if (string.Equals(arg, "24h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "1week", StringComparison.OrdinalIgnoreCase))
            {
                range = arg.ToLowerInvariant();
                continue;
            }

            if (string.Equals(arg, "--replace", StringComparison.OrdinalIgnoreCase))
            {
                replace = true;
                continue;
            }

            Console.WriteLine("Usage: dotnet run --project .\\WinTracker.Collector\\WinTracker.Collector.csproj -- seed [24h|1week] [--replace]");
            return true;
        }

        string sqlitePath = Path.Combine(baseDirectory, settings.SqliteFilePath);
        DateTimeOffset nowLocal = DateTimeOffset.Now;
        DateTimeOffset dayStartLocal = new(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);

        (DateTimeOffset fromUtc, DateTimeOffset toUtc) = range == "1week"
            ? (dayStartLocal.AddDays(-6).ToUniversalTime(), dayStartLocal.AddDays(1).ToUniversalTime())
            : (dayStartLocal.ToUniversalTime(), dayStartLocal.AddDays(1).ToUniversalTime());

        if (replace)
        {
            DeleteExistingSeedRows(sqlitePath, fromUtc, toUtc);
        }

        IReadOnlyList<AppEvent> events = BuildEvents(fromUtc, toUtc);
        using var writer = new SqliteEventWriter(sqlitePath);
        foreach (AppEvent appEvent in events)
        {
            writer.Write(appEvent);
        }

        Console.WriteLine($"Seed completed: rows={events.Count}, range={range}, replace={replace}, db={sqlitePath}");
        return true;
    }

    private static IReadOnlyList<AppEvent> BuildEvents(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        string[] apps = ["devenv.exe", "msedge.exe", "powershell.exe"];
        uint[] pids = [31612u, 18268u, 22452u];
        string[] hwnds = ["0x320B02", "0x204DE", "0x40912"];
        string[] titles =
        [
            "WinTracker - Program.cs - Microsoft Visual Studio",
            "Docs - Microsoft Edge",
            "Windows PowerShell"
        ];

        var rows = new List<AppEvent>();
        DateTimeOffset cursor = fromUtc;
        int dayIndex = 0;

        while (cursor < toUtc)
        {
            DateTimeOffset nextDay = cursor.AddDays(1);
            DateTimeOffset dayEnd = nextDay < toUtc ? nextDay : toUtc;

            for (DateTimeOffset hourStart = cursor; hourStart < dayEnd; hourStart = hourStart.AddHours(1))
            {
                DateTimeOffset nextHour = hourStart.AddHours(1);
                DateTimeOffset hourEnd = nextHour < dayEnd ? nextHour : dayEnd;
                if (hourEnd <= hourStart)
                {
                    continue;
                }

                int activeIndex = (dayIndex + hourStart.Hour) % apps.Length;

                for (int i = 0; i < apps.Length; i++)
                {
                    if (i == activeIndex)
                    {
                        int activeMinutes = 25 + ((dayIndex * 3 + hourStart.Hour * 5) % 30);
                        DateTimeOffset nextActive = hourStart.AddMinutes(activeMinutes);
                        DateTimeOffset activeEnd = nextActive < hourEnd ? nextActive : hourEnd;
                        rows.Add(CreateEvent(hourStart, activeEnd, apps[i], pids[i], hwnds[i], titles[i], "Active"));

                        if (activeEnd < hourEnd)
                        {
                            rows.Add(CreateEvent(activeEnd, hourEnd, apps[i], pids[i], hwnds[i], titles[i], "Open"));
                        }

                        continue;
                    }

                    // Add occasional "not running" gaps so timelines are easier to inspect.
                    if (((hourStart.Hour + i + dayIndex) % 8) == 0)
                    {
                        continue;
                    }

                    string state = ((hourStart.Hour + i + dayIndex) % 3) == 0 ? "Minimized" : "Open";
                    rows.Add(CreateEvent(hourStart, hourEnd, apps[i], pids[i], hwnds[i], titles[i], state));
                }
            }

            cursor = dayEnd;
            dayIndex++;
        }

        return rows;
    }

    private static AppEvent CreateEvent(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string exeName,
        uint pid,
        string hwnd,
        string title,
        string state)
    {
        return new AppEvent(
            StateStartUtc: startUtc,
            StateEndUtc: endUtc,
            ExeName: exeName,
            Pid: pid,
            Hwnd: hwnd,
            Title: title,
            State: state,
            Source: SeedSource);
    }

    private static void DeleteExistingSeedRows(string sqlitePath, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        string? directoryPath = Path.GetDirectoryName(sqlitePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadWriteCreate;Cache=Shared");
        connection.Open();

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

            DELETE FROM app_events
            WHERE source = $source
              AND state_end_utc > $from_utc
              AND state_start_utc < $to_utc;
            """;
        command.Parameters.AddWithValue("$source", SeedSource);
        command.Parameters.AddWithValue("$from_utc", fromUtc.ToString("O"));
        command.Parameters.AddWithValue("$to_utc", toUtc.ToString("O"));
        command.ExecuteNonQuery();
    }
}
