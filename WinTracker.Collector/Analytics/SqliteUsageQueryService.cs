using Microsoft.Data.Sqlite;
using WinTracker.Shared.Analytics;

internal sealed class SqliteUsageQueryService : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteUsageQueryService(string databasePath)
    {
        _connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Cache=Shared");
        _connection.Open();
    }

    public IReadOnlyList<AppStateUsageRow> QueryStateTotals(UsageQueryWindow window)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            WITH overlaps AS (
                SELECT
                    exe_name,
                    state,
                    MAX(julianday(state_start_utc), julianday($from_utc)) AS overlap_start,
                    MIN(julianday(state_end_utc), julianday($to_utc)) AS overlap_end
                FROM app_events
                WHERE state_end_utc > $from_utc
                  AND state_start_utc < $to_utc
            )
            SELECT
                exe_name,
                state,
                SUM((overlap_end - overlap_start) * 86400.0) AS seconds
            FROM overlaps
            WHERE overlap_end > overlap_start
            GROUP BY exe_name, state
            ORDER BY seconds DESC, exe_name ASC, state ASC;
            """;
        BindWindow(command, window);

        var rows = new List<AppStateUsageRow>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new AppStateUsageRow(
                ExeName: reader.GetString(0),
                State: reader.GetString(1),
                Seconds: reader.GetDouble(2)));
        }

        return rows;
    }

    public IReadOnlyList<AppUsageSummaryRow> QueryAppSummaries(UsageQueryWindow window)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            WITH overlaps AS (
                SELECT
                    exe_name,
                    state,
                    MAX(julianday(state_start_utc), julianday($from_utc)) AS overlap_start,
                    MIN(julianday(state_end_utc), julianday($to_utc)) AS overlap_end
                FROM app_events
                WHERE state_end_utc > $from_utc
                  AND state_start_utc < $to_utc
            ),
            durations AS (
                SELECT
                    exe_name,
                    state,
                    (overlap_end - overlap_start) * 86400.0 AS seconds
                FROM overlaps
                WHERE overlap_end > overlap_start
            )
            SELECT
                exe_name,
                SUM(seconds) AS total_seconds,
                SUM(CASE WHEN state = 'Active' THEN seconds ELSE 0 END) AS active_seconds,
                SUM(CASE WHEN state = 'Open' THEN seconds ELSE 0 END) AS open_seconds,
                SUM(CASE WHEN state = 'Minimized' THEN seconds ELSE 0 END) AS minimized_seconds
            FROM durations
            GROUP BY exe_name
            ORDER BY total_seconds DESC, exe_name ASC;
            """;
        BindWindow(command, window);

        var rows = new List<AppUsageSummaryRow>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new AppUsageSummaryRow(
                ExeName: reader.GetString(0),
                TotalSeconds: reader.GetDouble(1),
                ActiveSeconds: reader.GetDouble(2),
                OpenSeconds: reader.GetDouble(3),
                MinimizedSeconds: reader.GetDouble(4)));
        }

        return rows;
    }

    public IReadOnlyList<TimelineUsageRow> QueryTimeline(UsageQueryWindow window)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            WITH RECURSIVE buckets AS (
                SELECT
                    julianday($from_utc) AS bucket_start,
                    julianday($from_utc) + ($bucket_seconds / 86400.0) AS bucket_end
                UNION ALL
                SELECT
                    bucket_end,
                    bucket_end + ($bucket_seconds / 86400.0)
                FROM buckets
                WHERE bucket_end < julianday($to_utc)
            ),
            events AS (
                SELECT
                    exe_name,
                    state,
                    julianday(state_start_utc) AS event_start,
                    julianday(state_end_utc) AS event_end
                FROM app_events
                WHERE state_end_utc > $from_utc
                  AND state_start_utc < $to_utc
            ),
            overlaps AS (
                SELECT
                    b.bucket_start,
                    e.exe_name,
                    e.state,
                    MAX(e.event_start, b.bucket_start) AS overlap_start,
                    MIN(e.event_end, b.bucket_end) AS overlap_end
                FROM buckets b
                INNER JOIN events e
                    ON e.event_end > b.bucket_start
                   AND e.event_start < b.bucket_end
            )
            SELECT
                CAST(((bucket_start - 2440587.5) * 86400.0) AS INTEGER) AS bucket_start_unix,
                exe_name,
                state,
                SUM((overlap_end - overlap_start) * 86400.0) AS seconds
            FROM overlaps
            WHERE overlap_end > overlap_start
            GROUP BY bucket_start, exe_name, state
            ORDER BY bucket_start ASC, exe_name ASC, state ASC;
            """;
        BindWindow(command, window);
        command.Parameters.AddWithValue("$bucket_seconds", window.BucketSeconds);

        var rows = new List<TimelineUsageRow>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long unixSeconds = reader.GetInt64(0);
            rows.Add(new TimelineUsageRow(
                BucketStartUtc: DateTimeOffset.FromUnixTimeSeconds(unixSeconds),
                ExeName: reader.GetString(1),
                State: reader.GetString(2),
                Seconds: reader.GetDouble(3)));
        }

        return rows;
    }

    public void Dispose() => _connection.Dispose();

    private static void BindWindow(SqliteCommand command, UsageQueryWindow window)
    {
        command.Parameters.AddWithValue("$from_utc", window.FromUtc.ToString("O"));
        command.Parameters.AddWithValue("$to_utc", window.ToUtc.ToString("O"));
    }
}
