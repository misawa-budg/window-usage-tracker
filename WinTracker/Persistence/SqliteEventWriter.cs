using Microsoft.Data.Sqlite;

internal sealed class SqliteEventWriter : IAppEventWriter
{
    private const int BatchSize = 50;

    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _insertCommand;
    private readonly List<AppEvent> _buffer = [];
    private bool _disposed;

    public SqliteEventWriter(string databasePath)
    {
        string? directoryPath = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        _connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared");
        _connection.Open();
        InitializeSchema(_connection);

        _insertCommand = _connection.CreateCommand();
        _insertCommand.CommandText =
            """
            INSERT INTO app_events (
                event_at_utc,
                state_start_utc,
                state_end_utc,
                exe_name,
                pid,
                hwnd,
                title,
                state,
                source
            ) VALUES (
                $event_at_utc,
                $state_start_utc,
                $state_end_utc,
                $exe_name,
                $pid,
                $hwnd,
                $title,
                $state,
                $source
            );
            """;
        _insertCommand.Parameters.Add("$event_at_utc", SqliteType.Text);
        _insertCommand.Parameters.Add("$state_start_utc", SqliteType.Text);
        _insertCommand.Parameters.Add("$state_end_utc", SqliteType.Text);
        _insertCommand.Parameters.Add("$exe_name", SqliteType.Text);
        _insertCommand.Parameters.Add("$pid", SqliteType.Integer);
        _insertCommand.Parameters.Add("$hwnd", SqliteType.Text);
        _insertCommand.Parameters.Add("$title", SqliteType.Text);
        _insertCommand.Parameters.Add("$state", SqliteType.Text);
        _insertCommand.Parameters.Add("$source", SqliteType.Text);
    }

    public void Write(AppEvent appEvent)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteEventWriter));
        }

        _buffer.Add(appEvent);
        if (_buffer.Count >= BatchSize)
        {
            FlushBuffer();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            FlushBuffer();
        }
        finally
        {
            _insertCommand.Dispose();
            _connection.Dispose();
            _disposed = true;
        }
    }

    private static void InitializeSchema(SqliteConnection connection)
    {
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

            CREATE INDEX IF NOT EXISTS idx_app_events_time
            ON app_events(event_at_utc);

            CREATE INDEX IF NOT EXISTS idx_app_events_exe_time
            ON app_events(exe_name, event_at_utc);
            """;
        command.ExecuteNonQuery();
    }

    private void FlushBuffer()
    {
        if (_buffer.Count == 0)
        {
            return;
        }

        using var transaction = _connection.BeginTransaction();
        _insertCommand.Transaction = transaction;

        foreach (AppEvent appEvent in _buffer)
        {
            BindParameters(appEvent);
            _insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        _insertCommand.Transaction = null;
        _buffer.Clear();
    }

    private void BindParameters(AppEvent appEvent)
    {
        _insertCommand.Parameters["$event_at_utc"].Value = appEvent.StateEndUtc.ToString("O");
        _insertCommand.Parameters["$state_start_utc"].Value = appEvent.StateStartUtc.ToString("O");
        _insertCommand.Parameters["$state_end_utc"].Value = appEvent.StateEndUtc.ToString("O");
        _insertCommand.Parameters["$exe_name"].Value = appEvent.ExeName;
        _insertCommand.Parameters["$pid"].Value = (long)appEvent.Pid;
        _insertCommand.Parameters["$hwnd"].Value = appEvent.Hwnd;
        _insertCommand.Parameters["$title"].Value = appEvent.Title;
        _insertCommand.Parameters["$state"].Value = appEvent.State;
        _insertCommand.Parameters["$source"].Value = appEvent.Source;
    }
}
