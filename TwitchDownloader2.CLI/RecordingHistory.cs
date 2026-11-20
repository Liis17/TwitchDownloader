using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TwitchDownloader2.CLI
{
    public sealed record RecordingHistoryEntry(
        string SessionId,
        string Channel,
        DateTimeOffset StartedAt,
        DateTimeOffset RecordingEndedAt,
        DateTimeOffset CompletedAt,
        bool Succeeded,
        bool StoppedManually,
        double DurationSeconds,
        long SizeBytes,
        string RawPath,
        string? OutputPath,
        int AdvertisementCount,
        double AdvertisementDurationSeconds,
        int GapCount,
        string? ErrorMessage);

    /// <summary>
    /// История записей в локальной SQLite-базе: одна строка на завершённую запись.
    /// </summary>
    public sealed class RecordingHistoryStore
    {
        public static readonly string DefaultDatabasePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "history.db");

        private readonly object _gate = new();
        private readonly string _connectionString;

        public RecordingHistoryStore(string databasePath)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS recordings (
                        session_id TEXT PRIMARY KEY,
                        channel TEXT NOT NULL,
                        started_at TEXT NOT NULL,
                        recording_ended_at TEXT NOT NULL,
                        completed_at TEXT NOT NULL,
                        succeeded INTEGER NOT NULL,
                        stopped_manually INTEGER NOT NULL,
                        duration_seconds REAL NOT NULL,
                        size_bytes INTEGER NOT NULL,
                        raw_path TEXT NOT NULL,
                        output_path TEXT NULL,
                        advertisement_count INTEGER NOT NULL,
                        advertisement_duration_seconds REAL NOT NULL,
                        gap_count INTEGER NOT NULL,
                        error_message TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS ix_recordings_started_at ON recordings (started_at);
                    """;
                command.ExecuteNonQuery();
            }
        }

        public void Add(RecordingCompletionInfo info, DateTimeOffset completedAt)
        {
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT OR REPLACE INTO recordings (
                        session_id, channel, started_at, recording_ended_at, completed_at,
                        succeeded, stopped_manually, duration_seconds, size_bytes, raw_path, output_path,
                        advertisement_count, advertisement_duration_seconds, gap_count, error_message)
                    VALUES (
                        $sessionId, $channel, $startedAt, $recordingEndedAt, $completedAt,
                        $succeeded, $stoppedManually, $duration, $size, $rawPath, $outputPath,
                        $adCount, $adDuration, $gapCount, $error);
                    """;
                command.Parameters.AddWithValue("$sessionId", info.SessionId);
                command.Parameters.AddWithValue("$channel", info.Channel);
                command.Parameters.AddWithValue("$startedAt", FormatDate(info.StartedAt));
                command.Parameters.AddWithValue("$recordingEndedAt", FormatDate(info.RecordingEndedAt));
                command.Parameters.AddWithValue("$completedAt", FormatDate(completedAt));
                command.Parameters.AddWithValue("$succeeded", info.Succeeded ? 1 : 0);
                command.Parameters.AddWithValue("$stoppedManually", info.StoppedManually ? 1 : 0);
                command.Parameters.AddWithValue("$duration", info.DurationSeconds);
                command.Parameters.AddWithValue("$size", info.SizeBytes);
                command.Parameters.AddWithValue("$rawPath", info.RawPath);
                command.Parameters.AddWithValue("$outputPath", (object?)info.OutputPath ?? DBNull.Value);
                command.Parameters.AddWithValue("$adCount", info.AdvertisementCount);
                command.Parameters.AddWithValue("$adDuration", info.AdvertisementDurationSeconds);
                command.Parameters.AddWithValue("$gapCount", info.GapCount);
                command.Parameters.AddWithValue("$error", (object?)info.ErrorMessage ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        public IReadOnlyList<RecordingHistoryEntry> GetRecent(int limit)
        {
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT session_id, channel, started_at, recording_ended_at, completed_at,
                           succeeded, stopped_manually, duration_seconds, size_bytes, raw_path, output_path,
                           advertisement_count, advertisement_duration_seconds, gap_count, error_message
                    FROM recordings
                    ORDER BY started_at DESC
                    LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$limit", Math.Max(0, limit));

                var entries = new List<RecordingHistoryEntry>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    entries.Add(new RecordingHistoryEntry(
                        reader.GetString(0),
                        reader.GetString(1),
                        ParseDate(reader.GetString(2)),
                        ParseDate(reader.GetString(3)),
                        ParseDate(reader.GetString(4)),
                        reader.GetInt64(5) != 0,
                        reader.GetInt64(6) != 0,
                        reader.GetDouble(7),
                        reader.GetInt64(8),
                        reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.GetInt32(11),
                        reader.GetDouble(12),
                        reader.GetInt32(13),
                        reader.IsDBNull(14) ? null : reader.GetString(14)));
                }
                return entries;
            }
        }

        public int Count()
        {
            lock (_gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM recordings;";
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        // Единый UTC-формат: строковая сортировка по started_at совпадает с хронологической.
        private static string FormatDate(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

        private static DateTimeOffset ParseDate(string value) =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    /// <summary>
    /// Сохраняет итог каждой записи в историю и передаёт уведомления дальше.
    /// Сбой истории не мешает уведомлению владельца.
    /// </summary>
    public sealed class RecordingHistoryNotificationSink : IRecordingNotificationSink
    {
        private readonly RecordingHistoryStore _history;
        private readonly IRecordingNotificationSink _inner;

        public RecordingHistoryNotificationSink(RecordingHistoryStore history, IRecordingNotificationSink inner)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task RecordingStartedAsync(RecordingStartedInfo info, CancellationToken cancellationToken) =>
            _inner.RecordingStartedAsync(info, cancellationToken);

        public Task RecordingEndedAsync(RecordingEndedInfo info, CancellationToken cancellationToken) =>
            _inner.RecordingEndedAsync(info, cancellationToken);

        public Task RecordingCompletedAsync(RecordingCompletionInfo info, CancellationToken cancellationToken)
        {
            try
            {
                _history.Add(info, DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[History] Не удалось сохранить запись {info.SessionId} в историю: {ex.Message}");
            }

            return _inner.RecordingCompletedAsync(info, cancellationToken);
        }
    }
}
