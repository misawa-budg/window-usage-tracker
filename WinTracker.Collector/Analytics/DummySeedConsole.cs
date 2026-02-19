using Microsoft.Data.Sqlite;

internal static class DummySeedConsole
{
    private const string SeedSource = "demo-seed";

    private enum SeedProfile
    {
        Hourly,
        Mixed,
        Minute
    }

    public static bool TryHandle(string[] args, CollectorSettings settings, string baseDirectory)
    {
        if (args.Length == 0 || !string.Equals(args[0], "seed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string range = "24h";
        SeedProfile profile = SeedProfile.Hourly;
        bool replace = false;
        bool replaceAll = false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, "24h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "1week", StringComparison.OrdinalIgnoreCase))
            {
                range = arg.ToLowerInvariant();
                continue;
            }

            if (string.Equals(arg, "--profile", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || !TryParseProfile(args[i + 1], out profile))
                {
                    PrintUsage();
                    return true;
                }

                i++;
                continue;
            }

            if (TryParseProfile(arg, out SeedProfile inlineProfile))
            {
                profile = inlineProfile;
                continue;
            }

            if (string.Equals(arg, "--replace", StringComparison.OrdinalIgnoreCase))
            {
                replace = true;
                continue;
            }

            if (string.Equals(arg, "--replace-all", StringComparison.OrdinalIgnoreCase))
            {
                replaceAll = true;
                continue;
            }

            PrintUsage();
            return true;
        }

        string sqlitePath = Path.Combine(baseDirectory, settings.SqliteFilePath);
        DateTimeOffset nowLocal = DateTimeOffset.Now;
        DateTimeOffset dayStartLocal = new(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);

        (DateTimeOffset fromUtc, DateTimeOffset toUtc) = range == "1week"
            ? (dayStartLocal.AddDays(-6).ToUniversalTime(), dayStartLocal.AddDays(1).ToUniversalTime())
            : (dayStartLocal.ToUniversalTime(), dayStartLocal.AddDays(1).ToUniversalTime());

        if (replaceAll)
        {
            DeleteRows(sqlitePath, fromUtc, toUtc, seededOnly: false);
        }
        else if (replace)
        {
            DeleteRows(sqlitePath, fromUtc, toUtc, seededOnly: true);
        }

        IReadOnlyList<AppEvent> events = BuildEvents(fromUtc, toUtc, profile);
        using var writer = new SqliteEventWriter(sqlitePath);
        foreach (AppEvent appEvent in events)
        {
            writer.Write(appEvent);
        }

        Console.WriteLine(
            $"Seed completed: rows={events.Count}, range={range}, profile={profile.ToString().ToLowerInvariant()}, replace={replace}, replaceAll={replaceAll}, db={sqlitePath}");
        return true;
    }

    private static IReadOnlyList<AppEvent> BuildEvents(DateTimeOffset fromUtc, DateTimeOffset toUtc, SeedProfile profile)
    {
        return profile switch
        {
            SeedProfile.Hourly => BuildHourlyEvents(fromUtc, toUtc),
            SeedProfile.Mixed => BuildSegmentedEvents(
                fromUtc,
                toUtc,
                durationOptionsSeconds: [60, 120, 300, 600, 900, 1800, 2700],
                gapOptionsSeconds: [20, 40, 60, 120, 180, 300, 600]),
            SeedProfile.Minute => BuildSegmentedEvents(
                fromUtc,
                toUtc,
                durationOptionsSeconds: [60, 120, 180],
                gapOptionsSeconds: [0, 20, 30, 60]),
            _ => BuildHourlyEvents(fromUtc, toUtc)
        };
    }

    private static IReadOnlyList<AppEvent> BuildHourlyEvents(DateTimeOffset fromUtc, DateTimeOffset toUtc)
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

    private static IReadOnlyList<AppEvent> BuildSegmentedEvents(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<int> durationOptionsSeconds,
        IReadOnlyList<int> gapOptionsSeconds)
    {
        string[] apps = ["devenv.exe", "msedge.exe", "powershell.exe", "Code.exe", "Slack.exe"];
        uint[] pids = [31612u, 18268u, 22452u, 4120u, 9800u];
        string[] hwnds = ["0x320B02", "0x204DE", "0x40912", "0x125AA", "0x8332"];
        string[] titles =
        [
            "WinTracker - Program.cs - Microsoft Visual Studio",
            "Docs - Microsoft Edge",
            "Windows PowerShell",
            "WinTracker - Visual Studio Code",
            "Slack | WinTracker"
        ];

        var rows = new List<AppEvent>();
        const int maxEvents = 200_000;

        for (int appIndex = 0; appIndex < apps.Length; appIndex++)
        {
            DateTimeOffset cursor = fromUtc.AddMinutes(appIndex * 3);
            int step = 0;

            while (cursor < toUtc && rows.Count < maxEvents)
            {
                int gapSeconds = PickDeterministic(gapOptionsSeconds, appIndex, step, salt: 17);
                cursor = cursor.AddSeconds(gapSeconds);
                if (cursor >= toUtc)
                {
                    break;
                }

                int durationSeconds = PickDeterministic(durationOptionsSeconds, appIndex, step, salt: 31);
                DateTimeOffset end = cursor.AddSeconds(durationSeconds);
                if (end > toUtc)
                {
                    end = toUtc;
                }

                string state = ResolveState(appIndex, step);
                rows.Add(CreateEvent(cursor, end, apps[appIndex], pids[appIndex], hwnds[appIndex], titles[appIndex], state));

                cursor = end;
                step++;
            }
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

    private static void DeleteRows(string sqlitePath, DateTimeOffset fromUtc, DateTimeOffset toUtc, bool seededOnly)
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
            WHERE state_end_utc > $from_utc
              AND state_start_utc < $to_utc
              AND ($seed_only = 0 OR source = $source);
            """;
        command.Parameters.AddWithValue("$seed_only", seededOnly ? 1 : 0);
        command.Parameters.AddWithValue("$source", SeedSource);
        command.Parameters.AddWithValue("$from_utc", fromUtc.ToString("O"));
        command.Parameters.AddWithValue("$to_utc", toUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project .\\WinTracker.Collector\\WinTracker.Collector.csproj -- seed [24h|1week] [hourly|mixed|minute] [--profile <hourly|mixed|minute>] [--replace] [--replace-all]");
    }

    private static bool TryParseProfile(string value, out SeedProfile profile)
    {
        if (string.Equals(value, "hourly", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Hourly;
            return true;
        }

        if (string.Equals(value, "mixed", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Mixed;
            return true;
        }

        if (string.Equals(value, "minute", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Minute;
            return true;
        }

        profile = SeedProfile.Hourly;
        return false;
    }

    private static int PickDeterministic(IReadOnlyList<int> options, int appIndex, int step, int salt)
    {
        int hash = unchecked((appIndex + 1) * 1_000_003 + (step + 1) * 37 + salt * 97);
        int index = Math.Abs(hash) % options.Count;
        return options[index];
    }

    private static string ResolveState(int appIndex, int step)
    {
        if (((step + appIndex) % 7) == 0)
        {
            return "Active";
        }

        return ((step + appIndex) % 3) == 0 ? "Minimized" : "Open";
    }
}
