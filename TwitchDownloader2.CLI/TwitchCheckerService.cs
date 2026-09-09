namespace TwitchDownloader2.CLI
{
    public sealed class TwitchCheckerService : IDisposable
    {
        private static readonly string ServiceName = "TwitchChecker";
        private static readonly ConsoleColor ServiceColor = ConsoleColor.Magenta;

        private readonly AppSettings _settings;
        private readonly ITwitchDownloaderService _downloader;
        private readonly TimeSpan _checkInterval;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly SemaphoreSlim _forceCheck = new(0, 1);
        private readonly object _lifecycleGate = new();
        private Task? _workerTask;
        private bool _disposed;

        public TwitchCheckerService(
            AppSettings settings,
            ITwitchDownloaderService downloader,
            TimeSpan? checkInterval = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _checkInterval = checkInterval ?? TimeSpan.FromMinutes(1);
        }

        public void Start()
        {
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _workerTask ??= RunAsync(_cancellation.Token);
            }
        }

        public void ForceCheck()
        {
            if (_disposed || _forceCheck.CurrentCount > 0)
                return;

            try
            {
                _forceCheck.Release();
            }
            catch (SemaphoreFullException)
            {
                // Multiple Telegram clicks collapse into one immediate check.
            }
            catch (ObjectDisposedException)
            {
                // Shutdown won the race.
            }
        }

        public IReadOnlyDictionary<string, bool> GetStatuses()
        {
            var activeChannels = _downloader.GetActiveDownloads()
                .Select(download => download.Channel)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _settings.GetTrackedChannelsSnapshot()
                .ToDictionary(
                    channel => channel,
                    channel => activeChannels.Contains(channel),
                    StringComparer.OrdinalIgnoreCase);
        }

        internal async Task CheckNowAsync(CancellationToken cancellationToken)
        {
            var channels = _settings.GetTrackedChannelsSnapshot();
            if (channels.Count == 0)
            {
                ConsoleWriteLine("Список отслеживаемых каналов пуст. Ожидание...");
                return;
            }

            await Task.WhenAll(channels.Select(channel => CheckChannelAsync(channel, cancellationToken)));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task? worker;
            lock (_lifecycleGate)
            {
                _cancellation.Cancel();
                worker = _workerTask;
            }

            if (worker is null)
                return;

            try
            {
                await worker.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                // Normal checker shutdown.
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                while (!cancellationToken.IsCancellationRequested)
                {
                    await CheckNowAsync(cancellationToken);
                    await _forceCheck.WaitAsync(_checkInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Цикл проверки остановлен из-за ошибки: {ex.Message}", ConsoleColor.DarkRed);
            }
        }

        private async Task CheckChannelAsync(string channel, CancellationToken cancellationToken)
        {
            try
            {
                ConsoleWriteLine($"Проверка стрима на канале '{channel}'");
                var result = await _downloader.TryStartDownloadAsync(channel, cancellationToken);
                switch (result)
                {
                    case StartDownloadResult.Started:
                        ConsoleWriteLine($"Обнаружен стрим у '{channel}', запись запущена.");
                        break;
                    case StartDownloadResult.Suppressed:
                        ConsoleWriteLine($"'{channel}' остаётся live; повторный старт после ручной остановки запрещён.");
                        break;
                    case StartDownloadResult.Failed:
                        ConsoleWriteLine($"Проверка '{channel}' завершилась ошибкой.", ConsoleColor.DarkYellow);
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка проверки '{channel}': {ex.Message}", ConsoleColor.DarkRed);
            }
        }

        private static void ConsoleWriteLine(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("[");
            Console.ForegroundColor = ServiceColor;
            Console.Write(ServiceName);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = previousColor;
        }

        public void Dispose()
        {
            Task? worker;
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _cancellation.Cancel();
                worker = _workerTask;
            }

            try { worker?.Wait(TimeSpan.FromSeconds(3)); } catch { }
            _forceCheck.Dispose();
            _cancellation.Dispose();
        }
    }
}
