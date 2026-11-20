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

    public enum StartVodDownloadResult
    {
        Started,
        AlreadyActive,
        NotFound,
        Failed
    }

    public sealed record VodDownloadStart(StartVodDownloadResult Result, string? Channel, string? ErrorMessage);

    /// <summary>
    /// Откуда взялась сессия: запись отслеживаемого канала, ручная запись по кнопке «Загрузить»
    /// или загрузка VOD уже завершённого стрима.
    /// </summary>
    public enum RecordingKind
    {
        Tracked,
        Manual,
        Vod
    }

    public sealed record DownloadProgress(int SegmentsDone, int SegmentsTotal);

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
        ActiveDownloadState State,
        RecordingKind Kind = RecordingKind.Tracked,
        string? VodId = null,
        DownloadProgress? Progress = null);

    public sealed record RecordingStartedInfo(
        ActiveDownloadInfo Download,
        string SelectedQuality,
        bool IsSource);

    public sealed record RecordingEndedInfo(
        string SessionId,
        string Channel,
        double DurationSeconds,
        bool HasContent);

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
        string? ErrorMessage,
        DateTimeOffset StartedAt,
        DateTimeOffset RecordingEndedAt,
        bool StoppedManually,
        RecordingKind Kind = RecordingKind.Tracked,
        string? VodId = null);

    public interface IRecordingNotificationSink
    {
        Task RecordingStartedAsync(RecordingStartedInfo info, CancellationToken cancellationToken);
        Task RecordingEndedAsync(RecordingEndedInfo info, CancellationToken cancellationToken);
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

        private const int VodParallelism = 4;

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
            public RecordingKind Kind { get; init; } = RecordingKind.Tracked;
            public string? VodId { get; init; }
            public DownloadProgress? Progress { get; set; }
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

        public Task<StartDownloadResult> TryStartDownloadAsync(
            string channel,
            CancellationToken cancellationToken)
        {
            return StartLiveAsync(channel, RecordingKind.Tracked, cancellationToken);
        }

        /// <summary>
        /// Ручная запись текущего эфира по запросу владельца: канал не обязан быть отслеживаемым,
        /// пауза до offline не мешает явному запросу.
        /// </summary>
        public Task<StartDownloadResult> TryStartManualRecordingAsync(
            string channel,
            CancellationToken cancellationToken)
        {
            return StartLiveAsync(channel, RecordingKind.Manual, cancellationToken);
        }

        public async Task<VodDownloadStart> TryStartVodDownloadAsync(
            string vodId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(vodId) || !vodId.All(char.IsAsciiDigit))
                return new VodDownloadStart(StartVodDownloadResult.NotFound, null, null);

            lock (_gate)
            {
                if (_shutdownRequested)
                    return new VodDownloadStart(StartVodDownloadResult.Failed, null, "Приложение завершается.");
                if (_sessionsById.Values.Any(session => session.VodId == vodId))
                    return new VodDownloadStart(StartVodDownloadResult.AlreadyActive, null, null);
            }

            TwitchVodResult vod;
            try
            {
                vod = await _playbackClient.ResolveVodAsync(vodId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Не удалось получить VOD {vodId}: {ex.Message}", System.ConsoleColor.DarkYellow);
                return new VodDownloadStart(StartVodDownloadResult.Failed, null, ex.Message);
            }

            if (vod.Status == TwitchVodStatus.NotFound || vod.MediaPlaylistUrl is null)
                return new VodDownloadStart(StartVodDownloadResult.NotFound, null, null);

            if (!TryGetDownloadRoot(out var downloadRoot))
                return new VodDownloadStart(StartVodDownloadResult.Failed, vod.Channel, "Не удалось создать папку загрузок.");

            var sessionId = Guid.NewGuid().ToString("N");
            var startedAt = DateTimeOffset.UtcNow;
            var channelPart = string.IsNullOrWhiteSpace(vod.Channel) ? "unknown" : vod.Channel;
            var session = new DownloadSession
            {
                SessionId = sessionId,
                Channel = vod.Channel,
                StartedAt = startedAt,
                RawPath = Path.Combine(
                    downloadRoot,
                    $"vod_{channelPart}_{vodId}_{startedAt:yyyyMMdd-HHmmss}_{sessionId}.recording"),
                Playback = new TwitchPlaybackResult(
                    TwitchPlaybackStatus.Live,
                    vod.Channel,
                    vod.MediaPlaylistUrl,
                    vod.SelectedQuality,
                    vod.IsSource),
                Kind = RecordingKind.Vod,
                VodId = vodId
            };

            lock (_gate)
            {
                if (_shutdownRequested)
                {
                    session.Dispose();
                    return new VodDownloadStart(StartVodDownloadResult.Failed, vod.Channel, "Приложение завершается.");
                }

                if (_sessionsById.Values.Any(existing => existing.VodId == vodId))
                {
                    session.Dispose();
                    return new VodDownloadStart(StartVodDownloadResult.AlreadyActive, vod.Channel, null);
                }

                _sessionsById.Add(sessionId, session);
                session.WorkerTask = RunSessionAsync(session);
            }

            ConsoleWriteLine($"Загрузка VOD {vodId} канала '{vod.Channel}' запущена (сессия {sessionId}).");
            return new VodDownloadStart(StartVodDownloadResult.Started, vod.Channel, null);
        }

        private async Task<StartDownloadResult> StartLiveAsync(
            string channel,
            RecordingKind kind,
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

            if (kind == RecordingKind.Tracked && _settings.IsPausedUntilOffline(normalizedChannel))
                return StartDownloadResult.Suppressed;

            if (!playback.IsSource)
            {
                ConsoleWriteLine(
                    $"Source недоступен для '{normalizedChannel}', используется '{playback.SelectedQuality}'.",
                    System.ConsoleColor.DarkYellow);
            }

            if (!TryGetDownloadRoot(out var downloadRoot))
                return StartDownloadResult.Failed;

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
                Playback = playback,
                Kind = kind
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

                if (kind == RecordingKind.Tracked && _settings.IsPausedUntilOffline(normalizedChannel))
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

            // Пауза до offline нужна только отслеживаемым каналам: ручную запись и VOD никто не перезапускает.
            if (session.Kind != RecordingKind.Tracked)
            {
                CancelSafely(session.RecordingCancellation);
                ConsoleWriteLine($"Остановка сессии {session.SessionId} для '{session.Channel}' принята.");
                return Task.FromResult(StopDownloadResult.Accepted);
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
            DateTimeOffset? recordingEndedAt = null;
            try
            {
                // The start notification is best-effort and must not delay end/finalization.
                _ = NotifyStartedSafelyAsync(session);
                recording = session.Kind == RecordingKind.Vod
                    ? await _recorder.DownloadVodAsync(
                        $"VOD {session.VodId}",
                        session.Playback.MediaPlaylistUrl!,
                        session.RawPath,
                        VodParallelism,
                        (done, total) => UpdateProgress(session, done, total),
                        session.RecordingCancellation.Token)
                    : await _recorder.RecordAsync(
                        session.Channel,
                        session.Playback.MediaPlaylistUrl!,
                        session.RawPath,
                        session.RecordingCancellation.Token);
                recordingEndedAt = DateTimeOffset.UtcNow;

                // Для VOD эфир уже завершён: уведомление о конце трансляции не отправляется.
                var endedNaturally = session.Kind != RecordingKind.Vod
                    && recording.EndReason is RecordingEndReason.EndList or RecordingEndReason.Offline;

                lock (_gate)
                {
                    if (session.SkipFinalization)
                        return;
                    session.State = ActiveDownloadState.Finalizing;
                }

                var endedNotification = endedNaturally
                    ? NotifyEndedSafelyAsync(
                        new RecordingEndedInfo(
                            session.SessionId,
                            session.Channel,
                            recording.ContentDurationSeconds,
                            recording.HasContent),
                        session.FinalizationCancellation.Token)
                    : Task.CompletedTask;

                if (!recording.HasContent)
                {
                    await endedNotification;
                    if (endedNaturally)
                        ReleaseRecordingReservation(session);
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
                        recording.ErrorMessage ?? "Запись не содержит ни одного медиа-сегмента.",
                        session.StartedAt,
                        recordingEndedAt.Value,
                        IsStopRequested(session),
                        session.Kind,
                        session.VodId),
                        session.FinalizationCancellation.Token);
                    return;
                }

                var finalizationTask = _finalizer.FinalizeAsync(
                    recording.RawPath,
                    recording.ContentDurationSeconds,
                    recording.Container,
                    session.FinalizationCancellation.Token);
                await endedNotification;
                if (endedNaturally)
                    ReleaseRecordingReservation(session);
                var finalized = await finalizationTask;
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
                    finalized.ErrorMessage ?? recording.ErrorMessage,
                    session.StartedAt,
                    recordingEndedAt.Value,
                    IsStopRequested(session),
                    session.Kind,
                    session.VodId),
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
                        ex.Message,
                        session.StartedAt,
                        recordingEndedAt ?? DateTimeOffset.UtcNow,
                        IsStopRequested(session),
                        session.Kind,
                        session.VodId),
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
            var cancellationToken = session.RecordingCancellation.Token;
            try
            {
                await _notifications.RecordingStartedAsync(
                    new RecordingStartedInfo(ToInfo(session), session.Playback.SelectedQuality, session.Playback.IsSource),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

        private async Task NotifyEndedSafelyAsync(
            RecordingEndedInfo info,
            CancellationToken cancellationToken)
        {
            try
            {
                await _notifications.RecordingEndedAsync(info, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Docker shutdown cancels notification I/O as well as media tools.
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Не удалось отправить уведомление о завершении трансляции: {ex.Message}", System.ConsoleColor.DarkYellow);
            }
        }

        private void ReleaseRecordingReservation(DownloadSession session)
        {
            lock (_gate)
            {
                if (_sessionsByChannel.TryGetValue(session.Channel, out var current)
                    && ReferenceEquals(current, session))
                {
                    _sessionsByChannel.Remove(session.Channel);
                }
            }
        }

        private bool IsShutdown(DownloadSession session)
        {
            lock (_gate)
                return session.SkipFinalization || _shutdownRequested;
        }

        private void UpdateProgress(DownloadSession session, int done, int total)
        {
            lock (_gate)
                session.Progress = new DownloadProgress(done, total);
        }

        private bool TryGetDownloadRoot(out string downloadRoot)
        {
            downloadRoot = string.IsNullOrWhiteSpace(_settings.DownloadPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads")
                : _settings.DownloadPath;
            try
            {
                Directory.CreateDirectory(downloadRoot);
                return true;
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Не удалось создать папку загрузок: {ex.Message}", System.ConsoleColor.DarkRed);
                return false;
            }
        }

        private bool IsStopRequested(DownloadSession session)
        {
            lock (_gate)
                return session.StopRequested;
        }

        private static ActiveDownloadInfo ToInfo(DownloadSession session)
        {
            return new ActiveDownloadInfo(
                session.SessionId,
                session.Channel,
                session.StartedAt,
                session.RawPath,
                session.State,
                session.Kind,
                session.VodId,
                session.Progress);
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
