using Microsoft.Data.Sqlite;
using WinTracker.Shared.Analytics;

namespace WinTracker.Viewer;

internal sealed class SqliteTimelineQueryService : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTimelineQueryService(string databasePath)
    {
        _connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Cache=Shared");
        _connection.Open();
    }

    public IReadOnlyList<TimelineUsageRow> QueryTimeline(UsageQueryWindow window)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            WITH RECURSIVE buckets AS (
                SELECT
                    unixepoch($from_utc) AS bucket_start,
                    unixepoch($from_utc) + $bucket_seconds AS bucket_end
                UNION ALL
                SELECT
                    bucket_end,
                    bucket_end + $bucket_seconds
                FROM buckets
                WHERE bucket_end < unixepoch($to_utc)
            ),
            events AS (
                SELECT
                    exe_name,
                    state,
                    unixepoch(state_start_utc) AS event_start,
                    unixepoch(state_end_utc) AS event_end
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
                bucket_start AS bucket_start_unix,
                exe_name,
                state,
                SUM(overlap_end - overlap_start) AS seconds
            FROM overlaps
            WHERE overlap_end > overlap_start
            GROUP BY bucket_start, exe_name, state
            ORDER BY bucket_start ASC, exe_name ASC, state ASC;
            """;

        command.Parameters.AddWithValue("$from_utc", window.FromUtc.ToString("O"));
        command.Parameters.AddWithValue("$to_utc", window.ToUtc.ToString("O"));
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
}
