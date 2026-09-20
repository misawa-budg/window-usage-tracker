using Microsoft.Data.Sqlite;
using WinTracker.Shared.Analytics;

namespace WinTracker.Viewer;

internal sealed class SqliteTimelineQueryService : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly bool _includeDemo;
    private readonly bool _hasServices;

    public SqliteTimelineQueryService(string databasePath, bool includeDemo = false)
    {
        _includeDemo = includeDemo;
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared
        }.ToString());
        _connection.Open();
        using var schema = _connection.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM pragma_table_info('app_events') WHERE name = 'service_id';";
        _hasServices = Convert.ToInt32(schema.ExecuteScalar()) != 0;
    }

    public IReadOnlyList<AppStateIntervalRow> QueryStateIntervals(UsageQueryWindow window)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT
                exe_name,
                state,
                state_start_utc,
                state_end_utc,
                {(_hasServices ? "service_id" : "NULL")} AS service_id
            FROM app_events
            WHERE state_end_utc > $from_utc
              AND state_start_utc < $to_utc
                  AND ($include_demo = 1 OR source <> 'demo-seed')
            ORDER BY state_start_utc ASC;
            """;

        BindWindow(command, window);

        var rows = new List<AppStateIntervalRow>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            DateTimeOffset startUtc = DateTimeOffset.Parse(reader.GetString(2));
            DateTimeOffset endUtc = DateTimeOffset.Parse(reader.GetString(3));
            if (!TryClipToWindow(startUtc, endUtc, window, out DateTimeOffset clippedStart, out DateTimeOffset clippedEnd))
            {
                continue;
            }

            rows.Add(new AppStateIntervalRow(
                ExeName: reader.GetString(0),
                State: reader.GetString(1),
                StateStartUtc: clippedStart,
                StateEndUtc: clippedEnd,
                ServiceId: reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    private void BindWindow(SqliteCommand command, UsageQueryWindow window)
    {
        window.Validate();
        command.Parameters.AddWithValue("$include_demo", _includeDemo ? 1 : 0);
        command.Parameters.AddWithValue("$from_utc", window.FromUtc.ToString("O"));
        command.Parameters.AddWithValue("$to_utc", window.ToUtc.ToString("O"));
    }

    private static bool TryClipToWindow(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        UsageQueryWindow window,
        out DateTimeOffset clippedStart,
        out DateTimeOffset clippedEnd)
    {
        clippedStart = startUtc < window.FromUtc ? window.FromUtc : startUtc;
        clippedEnd = endUtc > window.ToUtc ? window.ToUtc : endUtc;
        return clippedEnd > clippedStart;
    }

    public void Dispose() => _connection.Dispose();
}
