using System.Diagnostics;
using System.Text;

namespace TwitchDownloader2.CLI
{
    public class TwitchCheckerService : IDisposable
    {
        private static readonly string _serviceName = "TwitchChecker";
        private static readonly ConsoleColor _consoleColor = ConsoleColor.Magenta;

        private readonly Thread _workerThread;
        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _forceCheckEvent = new(false);
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
        private readonly object _stateLock = new();
        private readonly Dictionary<string, ChannelDownloadState> _channelStates = new(StringComparer.OrdinalIgnoreCase);

        private sealed class ChannelDownloadState
        {
            public string Channel { get; init; } = string.Empty;
            public bool IsDownloading { get; set; }
            public List<int> Pids { get; } = new();
        }

        public TwitchCheckerService()
        {
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = nameof(TwitchCheckerService)
            };
            _workerThread.Start();
        }

        // Принудительный запуск проверки (сброс ожидания)
        public void ForceCheck()
        {
            try { _forceCheckEvent.Set(); } catch { /* ignored */ }
        }

        // Пометить канал как завершивший загрузку (на будущее расширение API)
        public void MarkDownloadFinished(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return;
            lock (_stateLock)
            {
                if (_channelStates.TryGetValue(channel, out var state))
                {
                    state.IsDownloading = false;
                    state.Pids.Clear();
                }
            }
        }

        private void WorkerLoop()
        {
            // Небольшая пауза на инициализацию остальных сервисов
            try { Thread.Sleep(TimeSpan.FromSeconds(2)); } catch { }

            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var channels = (Program.Settings?.TrackedChannels ?? new List<string>())
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => c.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (channels.Count == 0)
                    {
                        ConsoleWriteLine("Список отслеживаемых каналов пуст. Ожидание...");
                    }
                    else
                    {
                        foreach (var channel in channels)
                        {
                            if (token.IsCancellationRequested) break;

                            if (IsDownloading(channel))
                            {
                                // Уже идет загрузка — пропускаем
                                continue;
                            }

                            ConsoleWriteLine($"Проверка стрима на канале '{channel}'");
                            bool isLive = IsChannelLive(channel, token);
                            if (isLive)
                            {
                                // Проверяем и помечаем, чтобы избежать дублей
                                if (TryMarkDownloadStarted(channel))
                                {
                                    ConsoleWriteLine($"Обнаружен стрим у канала '{channel}'. Запуск загрузки...");
                                    try
                                    {
                                        if (Program.TwitchDownloader != null)
                                        {
                                            Program.TwitchDownloader.StartDownload(channel);
                                        }
                                        else
                                        {
                                            ConsoleWriteLine("Сервис загрузки еще не инициализирован. Пропуск запуска.", ConsoleColor.DarkYellow);
                                            // Сброс отметки, чтобы проверить снова на следующем цикле
                                            MarkDownloadFinished(channel);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        ConsoleWriteLine($"Ошибка при запуске загрузки: {ex.Message}", ConsoleColor.DarkRed);
                                        // Сброс отметки, чтобы можно было повторить попытку позже
                                        MarkDownloadFinished(channel);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWriteLine($"Ошибка цикла проверки: {ex.Message}", ConsoleColor.DarkRed);
                }

                // Ожидание следующего цикла, с возможностью принудительного сброса
                int signaledIndex;
                try
                {
                    signaledIndex = WaitHandle.WaitAny(new WaitHandle[] { _cts.Token.WaitHandle, _forceCheckEvent }, _checkInterval);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (signaledIndex == 0)
                {
                    // Отмена
                    break;
                }
                // signaledIndex == 1 — ForceCheck, просто продолжаем без задержки
                // WaitTimeout — истек интервал, продолжаем плановую проверку
            }
        }

        private bool IsDownloading(string channel)
        {
            lock (_stateLock)
            {
                return _channelStates.TryGetValue(channel, out var s) && s.IsDownloading;
            }
        }

        private bool TryMarkDownloadStarted(string channel)
        {
            lock (_stateLock)
            {
                if (_channelStates.TryGetValue(channel, out var state))
                {
                    if (state.IsDownloading) return false;
                    state.IsDownloading = true;
                    return true;
                }
                else
                {
                    _channelStates[channel] = new ChannelDownloadState
                    {
                        Channel = channel,
                        IsDownloading = true
                    };
                    return true;
                }
            }
        }

        private bool IsChannelLive(string channel, CancellationToken token)
        {
            // Аналог .cmd: yt-dlp --quiet --no-warnings --print title "https://www.twitch.tv/<channel>"
            // Возврат 0 — стрим идет, иначе — нет
            var url = $"https://www.twitch.tv/{channel}";

            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp", // предполагаем, что в PATH
                Arguments = $"--quiet --no-warnings --print title \"{url}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                if (!proc.Start())
                {
                    ConsoleWriteLine("Не удалось запустить yt-dlp. Убедитесь, что он установлен и доступен в PATH.", ConsoleColor.DarkYellow);
                    return false;
                }

                // Ждем завершения, но с таймаутом чтобы не зависнуть
                var exited = proc.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
                if (!exited)
                {
                    try { proc.Kill(true); } catch { }
                    ConsoleWriteLine("Проверка статуса канала превысила таймаут (30s).", ConsoleColor.DarkYellow);
                    return false;
                }

                // Аналог проверки errorlevel из .cmd
                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка запуска yt-dlp: {ex.Message}", ConsoleColor.DarkRed);
                return false;
            }
        }

        private static void ConsoleWriteLine(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("[");
            Console.ForegroundColor = _consoleColor;
            Console.Write(_serviceName);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = previousColor;
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _forceCheckEvent.Set(); } catch { }

            try
            {
                if (_workerThread.IsAlive)
                {
                    // Даем немного времени для корректного завершения
                    if (!_workerThread.Join((int)TimeSpan.FromSeconds(3).TotalMilliseconds))
                    {
                        try { _workerThread.Interrupt(); } catch { }
                    }
                }
            }
            catch { }

            try { _cts.Dispose(); } catch { }
            try { _forceCheckEvent.Dispose(); } catch { }
        }
    }
}
