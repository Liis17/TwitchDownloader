namespace TwitchDownloader2.CLI
{
    public enum StartDownloadResult
    {
        Started,
        AlreadyActive,
        Offline,
        Suppressed,
        Failed
    }

    public enum StopDownloadResult
    {
        Accepted,
        AlreadyStopping,
        NotFound
    }

    public enum ActiveDownloadState
    {
        Recording,
        Finalizing
    }

    public sealed record ActiveDownloadInfo(
        string SessionId,
        string Channel,
        DateTimeOffset StartedAt,
        string Path,
        ActiveDownloadState State);

    public sealed record RecordingStartedInfo(
        ActiveDownloadInfo Download,
        string SelectedQuality,
        bool IsSource);

    public sealed record RecordingCompletionInfo(
        string SessionId,
        string Channel,
        bool Succeeded,
        string RawPath,
        string? OutputPath,
        double DurationSeconds,
        long SizeBytes,
        int AdvertisementCount,
        double AdvertisementDurationSeconds,
        int GapCount,
        string? ErrorMessage);

    public interface IRecordingNotificationSink
    {
        Task RecordingStartedAsync(RecordingStartedInfo info, CancellationToken cancellationToken);
        Task RecordingCompletedAsync(RecordingCompletionInfo info, CancellationToken cancellationToken);
    }

    public interface ITwitchDownloaderService
    {
        Task<StartDownloadResult> TryStartDownloadAsync(string channel, CancellationToken cancellationToken);
        Task<StopDownloadResult> RequestStopAsync(string sessionId, CancellationToken cancellationToken);
        IReadOnlyList<ActiveDownloadInfo> GetActiveDownloads();
        Task StopForShutdownAsync(CancellationToken cancellationToken);
    }

    public sealed class TwitchDownloaderService : ITwitchDownloaderService
    {
        private static readonly string ServiceName = "TwitchDownloader";
        private static readonly ConsoleColor ConsoleColor = System.ConsoleColor.DarkCyan;

        private readonly object _gate = new();
        private readonly AppSettings _settings;
        private readonly ITwitchPlaybackClient _playbackClient;
        private readonly LiveStreamRecorder _recorder;
        private readonly MediaFinalizer _finalizer;
        private readonly IRecordingNotificationSink _notifications;
        private readonly Action _saveSettings;
        private readonly Dictionary<string, DownloadSession> _sessionsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DownloadSession> _sessionsByChannel = new(StringComparer.OrdinalIgnoreCase);
        private bool _shutdownRequested;

        private sealed class DownloadSession : IDisposable
        {
            public required string SessionId { get; init; }
            public required string Channel { get; init; }
            public required DateTimeOffset StartedAt { get; init; }
            public required string RawPath { get; init; }
            public required TwitchPlaybackResult Playback { get; init; }
            public CancellationTokenSource RecordingCancellation { get; } = new();
            public CancellationTokenSource FinalizationCancellation { get; } = new();
            public ActiveDownloadState State { get; set; } = ActiveDownloadState.Recording;
            public bool StopRequested { get; set; }
            public bool SkipFinalization { get; set; }
            public Task WorkerTask { get; set; } = Task.CompletedTask;

            public void Dispose()
            {
                RecordingCancellation.Dispose();
                FinalizationCancellation.Dispose();
            }
        }

        public TwitchDownloaderService(
            AppSettings settings,
            ITwitchPlaybackClient playbackClient,
            IRecordingNotificationSink notifications)
            : this(
                settings,
                playbackClient,
                new ProcessMediaToolRunner(),
                notifications,
                settings.SaveOrThrow,
                new SystemAsyncDelay())
        {
        }

        internal TwitchDownloaderService(
            AppSettings settings,
            ITwitchPlaybackClient playbackClient,
            IMediaToolRunner mediaToolRunner,
            IRecordingNotificationSink notifications,
            Action saveSettings,
            IAsyncDelay delay)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _playbackClient = playbackClient ?? throw new ArgumentNullException(nameof(playbackClient));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
            _recorder = new LiveStreamRecorder(_playbackClient, delay, message => ConsoleWriteLine(message));
            _finalizer = new MediaFinalizer(mediaToolRunner, message => ConsoleWriteLine(message));
        }

        public async Task<StartDownloadResult> TryStartDownloadAsync(
            string channel,
            CancellationToken cancellationToken)
        {
            string normalizedChannel;
            try
            {
                normalizedChannel = NormalizeChannel(channel);
            }
            catch (ArgumentException ex)
            {
                ConsoleWriteLine(ex.Message, System.ConsoleColor.DarkYellow);
                return StartDownloadResult.Failed;
            }

            lock (_gate)
            {
                if (_shutdownRequested)
                    return StartDownloadResult.Failed;
                if (_sessionsByChannel.ContainsKey(normalizedChannel))
                    return StartDownloadResult.AlreadyActive;
            }

            TwitchPlaybackResult playback;
            try
            {
                playback = await _playbackClient.ResolveLiveAsync(normalizedChannel, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ConsoleWriteLine(
                    $"Не удалось проверить канал '{normalizedChannel}': {ex.Message}",
                    System.ConsoleColor.DarkYellow);
                return StartDownloadResult.Failed;
            }

            if (playback.Status == TwitchPlaybackStatus.Offline)
            {
                if (_settings.RemovePausedChannel(normalizedChannel))
                {
                    try
                    {
                        _saveSettings();
                    }
                    catch (Exception ex)
                    {
                        _settings.AddPausedChannel(normalizedChannel);
                        ConsoleWriteLine(
                            $"Offline подтверждён, но не удалось сохранить снятие паузы '{normalizedChannel}': {ex.Message}",
                            System.ConsoleColor.DarkRed);
                        return StartDownloadResult.Failed;
                    }
                }
                return StartDownloadResult.Offline;
            }

            if (playback.MediaPlaylistUrl is null)
            {
                ConsoleWriteLine($"Twitch не вернул media playlist для '{normalizedChannel}'.", System.ConsoleColor.DarkYellow);
                return StartDownloadResult.Failed;
            }

            if (_settings.IsPausedUntilOffline(normalizedChannel))
                return StartDownloadResult.Suppressed;

            if (!playback.IsSource)
            {
                ConsoleWriteLine(
                    $"Source недоступен для '{normalizedChannel}', используется '{playback.SelectedQuality}'.",
                    System.ConsoleColor.DarkYellow);
            }

            string downloadRoot;
            try
            {
                downloadRoot = string.IsNullOrWhiteSpace(_settings.DownloadPath)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads")
                    : _settings.DownloadPath;
                Directory.CreateDirectory(downloadRoot);
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Не удалось создать папку загрузок: {ex.Message}", System.ConsoleColor.DarkRed);
                return StartDownloadResult.Failed;
            }

            var sessionId = Guid.NewGuid().ToString("N");
            var startedAt = DateTimeOffset.UtcNow;
            var rawPath = Path.Combine(
                downloadRoot,
                $"live_{normalizedChannel}_{startedAt:yyyyMMdd-HHmmss}_source_{sessionId}.recording");
            var session = new DownloadSession
            {
                SessionId = sessionId,
                Channel = normalizedChannel,
                StartedAt = startedAt,
                RawPath = rawPath,
                Playback = playback
            };

            lock (_gate)
            {
                if (_shutdownRequested)
                {
                    session.Dispose();
                    return StartDownloadResult.Failed;
                }

                if (_sessionsByChannel.ContainsKey(normalizedChannel))
                {
                    session.Dispose();
                    return StartDownloadResult.AlreadyActive;
                }

                if (_settings.IsPausedUntilOffline(normalizedChannel))
                {
                    session.Dispose();
                    return StartDownloadResult.Suppressed;
                }

                _sessionsById.Add(sessionId, session);
                _sessionsByChannel.Add(normalizedChannel, session);
                session.WorkerTask = RunSessionAsync(session);
            }

            ConsoleWriteLine($"Запись '{normalizedChannel}' запущена (сессия {sessionId}).");
            return StartDownloadResult.Started;
        }

        public Task<StopDownloadResult> RequestStopAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(sessionId))
                return Task.FromResult(StopDownloadResult.NotFound);

            DownloadSession session;
            lock (_gate)
            {
                if (!_sessionsById.TryGetValue(sessionId, out session!))
                    return Task.FromResult(StopDownloadResult.NotFound);

                if (session.StopRequested || session.State == ActiveDownloadState.Finalizing)
                    return Task.FromResult(StopDownloadResult.AlreadyStopping);

                session.StopRequested = true;
            }

            var pauseAdded = _settings.AddPausedChannel(session.Channel);
            try
            {
                _saveSettings();
            }
            catch (Exception ex)
            {
                if (pauseAdded)
                    _settings.RemovePausedChannel(session.Channel);
                lock (_gate)
                {
                    if (_sessionsById.ContainsKey(session.SessionId)
                        && session.State == ActiveDownloadState.Recording)
                    {
                        session.StopRequested = false;
                    }
                }
                throw new InvalidOperationException(
                    $"Не удалось сохранить паузу для канала '{session.Channel}'; запись продолжена.",
                    ex);
            }

            CancelSafely(session.RecordingCancellation);
            ConsoleWriteLine($"Остановка сессии {session.SessionId} для '{session.Channel}' принята.");
            return Task.FromResult(StopDownloadResult.Accepted);
        }

        public IReadOnlyList<ActiveDownloadInfo> GetActiveDownloads()
        {
            lock (_gate)
            {
                return _sessionsById.Values
                    .Select(ToInfo)
                    .OrderBy(info => info.StartedAt)
                    .ToArray();
            }
        }

        public async Task StopForShutdownAsync(CancellationToken cancellationToken)
        {
            DownloadSession[] sessions;
            lock (_gate)
            {
                _shutdownRequested = true;
                sessions = _sessionsById.Values.ToArray();
                foreach (var session in sessions)
                    session.SkipFinalization = true;
            }

            foreach (var session in sessions)
            {
                CancelSafely(session.RecordingCancellation);
                CancelSafely(session.FinalizationCancellation);
            }

            if (sessions.Length == 0)
                return;

            await Task.WhenAll(sessions.Select(session => session.WorkerTask)).WaitAsync(cancellationToken);
        }

        internal Task<IReadOnlyList<MediaFinalizeResult>> RecoverInterruptedDownloadsAsync(
            CancellationToken cancellationToken)
        {
            var downloadRoot = string.IsNullOrWhiteSpace(_settings.DownloadPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads")
                : _settings.DownloadPath;
            return _finalizer.RecoverOrphansAsync(downloadRoot, cancellationToken);
        }

        private async Task RunSessionAsync(DownloadSession session)
        {
            await Task.Yield();
            LiveRecordingResult? recording = null;
            try
            {
                var startedNotification = NotifyStartedSafelyAsync(session);
                recording = await _recorder.RecordAsync(
                    session.Channel,
                    session.Playback.MediaPlaylistUrl!,
                    session.RawPath,
                    session.RecordingCancellation.Token);
                await startedNotification;

                lock (_gate)
                {
                    if (session.SkipFinalization)
                        return;
                    session.State = ActiveDownloadState.Finalizing;
                }

                if (!recording.HasContent)
                {
                    await NotifyCompletedSafelyAsync(new RecordingCompletionInfo(
                        session.SessionId,
                        session.Channel,
                        false,
                        recording.RawPath,
                        null,
                        0,
                        0,
                        recording.AdvertisementCount,
                        recording.AdvertisementDurationSeconds,
                        recording.GapCount,
                        recording.ErrorMessage ?? "Запись не содержит ни одного медиа-сегмента."),
                        session.FinalizationCancellation.Token);
                    return;
                }

                var finalized = await _finalizer.FinalizeAsync(
                    recording.RawPath,
                    recording.ContentDurationSeconds,
                    recording.Container,
                    session.FinalizationCancellation.Token);
                var outputPath = finalized.Status == MediaFinalizeStatus.NoContent
                    ? null
                    : finalized.OutputPath;
                var size = outputPath is not null && File.Exists(outputPath)
                    ? new FileInfo(outputPath).Length
                    : 0;
                var duration = finalized.VideoDurationSeconds > 0
                    ? finalized.VideoDurationSeconds
                    : finalized.ExpectedDurationSeconds;

                await NotifyCompletedSafelyAsync(new RecordingCompletionInfo(
                    session.SessionId,
                    session.Channel,
                    finalized.Status == MediaFinalizeStatus.Succeeded,
                    finalized.RawPath,
                    outputPath,
                    duration,
                    size,
                    recording.AdvertisementCount,
                    recording.AdvertisementDurationSeconds,
                    recording.GapCount,
                    finalized.ErrorMessage ?? recording.ErrorMessage),
                    session.FinalizationCancellation.Token);
            }
            catch (OperationCanceledException) when (IsShutdown(session))
            {
                ConsoleWriteLine($"Сессия {session.SessionId} остановлена для быстрого завершения процесса.");
            }
            catch (Exception ex)
            {
                ConsoleWriteLine(
                    $"Ошибка сессии {session.SessionId} канала '{session.Channel}': {ex.Message}",
                    System.ConsoleColor.DarkRed);

                if (!IsShutdown(session))
                {
                    await NotifyCompletedSafelyAsync(new RecordingCompletionInfo(
                        session.SessionId,
                        session.Channel,
                        false,
                        recording?.RawPath ?? session.RawPath,
                        null,
                        recording?.ContentDurationSeconds ?? 0,
                        0,
                        recording?.AdvertisementCount ?? 0,
                        recording?.AdvertisementDurationSeconds ?? 0,
                        recording?.GapCount ?? 0,
                        ex.Message),
                        CancellationToken.None);
                }
            }
            finally
            {
                lock (_gate)
                {
                    _sessionsById.Remove(session.SessionId);
                    if (_sessionsByChannel.TryGetValue(session.Channel, out var current)
                        && ReferenceEquals(current, session))
                    {
                        _sessionsByChannel.Remove(session.Channel);
                    }
                }

                session.Dispose();
                ConsoleWriteLine($"Сессия {session.SessionId} канала '{session.Channel}' завершена.");
            }
        }

        private async Task NotifyStartedSafelyAsync(DownloadSession session)
        {
            try
            {
                await _notifications.RecordingStartedAsync(
                    new RecordingStartedInfo(ToInfo(session), session.Playback.SelectedQuality, session.Playback.IsSource),
                    session.RecordingCancellation.Token);
            }
            catch (OperationCanceledException) when (session.RecordingCancellation.IsCancellationRequested)
            {
                // A stop request must not be delayed by a Telegram notification.
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Не удалось отправить уведомление о старте: {ex.Message}", System.ConsoleColor.DarkYellow);
            }
        }

        private async Task NotifyCompletedSafelyAsync(
            RecordingCompletionInfo info,
            CancellationToken cancellationToken)
        {
            try
            {
                await _notifications.RecordingCompletedAsync(info, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Docker shutdown cancels notification I/O as well as media tools.
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Не удалось отправить уведомление о завершении: {ex.Message}", System.ConsoleColor.DarkYellow);
            }
        }

        private bool IsShutdown(DownloadSession session)
        {
            lock (_gate)
                return session.SkipFinalization || _shutdownRequested;
        }

        private static ActiveDownloadInfo ToInfo(DownloadSession session)
        {
            return new ActiveDownloadInfo(
                session.SessionId,
                session.Channel,
                session.StartedAt,
                session.RawPath,
                session.State);
        }

        private static void CancelSafely(CancellationTokenSource cancellation)
        {
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }

        private static string NormalizeChannel(string channel)
        {
            var normalized = channel?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized.Length is < 3 or > 25
                || normalized.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            {
                throw new ArgumentException("Имя Twitch-канала должно содержать 3–25 латинских букв, цифр или подчёркиваний.");
            }
            return normalized;
        }

        private static void ConsoleWriteLine(string message, System.ConsoleColor color = System.ConsoleColor.Gray)
        {
            var previousColor = System.Console.ForegroundColor;
            System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
            System.Console.Write("[");
            System.Console.ForegroundColor = ConsoleColor;
            System.Console.Write(ServiceName);
            System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
            System.Console.Write("] ");
            System.Console.ForegroundColor = color;
            System.Console.WriteLine(message);
            System.Console.ForegroundColor = previousColor;
        }
    }
}
