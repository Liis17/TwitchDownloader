using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TwitchDownloader2.CLI
{
    public class TwitchDownloaderService
    {
        private static readonly string _serviceName = "TwitchDownloader";
        private static readonly ConsoleColor _consoleColor = ConsoleColor.DarkCyan;

        private readonly string _downloadRoot;
        private readonly Random _rng = new();
        private readonly ConcurrentDictionary<string, DownloadSession> _activeSessions
            = new(StringComparer.OrdinalIgnoreCase);

        private sealed record DownloadFiles(string Video1, string Video2, string Audio1, string Audio2);

        private sealed class DownloadSession
        {
            public string Channel = string.Empty;
            public string SessionCode = string.Empty;
            public readonly List<Process> Processes = new();
            public readonly object ProcessesLock = new();
            public volatile bool ForcedByUser;
        }

        public TwitchDownloaderService(string downloadPath)
        {
            _downloadRoot = string.IsNullOrWhiteSpace(downloadPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads")
                : downloadPath;

            try { Directory.CreateDirectory(_downloadRoot); }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Не удалось создать папку загрузок: {_downloadRoot}. Ошибка: {ex.Message}", ConsoleColor.DarkRed);
            }
        }

        public async Task StartDownload(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                ConsoleWriteLine("Имя канала не задано", ConsoleColor.DarkYellow);
                return;
            }

            var channel = channelName.Trim();
            var sessionCode = GenerateCode(6);
            ConsoleWriteLine($"Старт загрузки канала '{channel}' (сессия {sessionCode})");

            var message = $"" +
            $"✨ У <b>{channel}</b> началась транслиция!\n" +
            $"\n" +
            $"⬇️  Скачивание запущено!\n" +
            $"\n" +
            $"🔔 По завершению стрима придет уведомление";
            await Program.TelegramServiceInstance.SendNotification(message);
            Thread.Sleep(1000);

            var files = new DownloadFiles(
                Path.Combine(_downloadRoot, $"{channel}_video_1_{sessionCode}.ts"),
                Path.Combine(_downloadRoot, $"{channel}_video_2_{sessionCode}.ts"),
                Path.Combine(_downloadRoot, $"{channel}_audio_1_{sessionCode}.aac"),
                Path.Combine(_downloadRoot, $"{channel}_audio_2_{sessionCode}.aac"));

            var message2 = $"" +
                $"📂 <b>Файлы этой транцляции:</b>\n" +
                $"<pre>🎞️ {files.Video1}\n" +
                $"🎞️ {files.Video2}\n" +
                $"🎵 {files.Audio1}\n" +
                $"🎵 {files.Audio2}</pre>";

            await Program.TelegramServiceInstance.SendNotification(message2);

            var hlsUrl = ResolveHlsUrl(channel);
            if (string.IsNullOrWhiteSpace(hlsUrl))
            {
                ConsoleWriteLine("Не удалось получить HLS URL через yt-dlp", ConsoleColor.DarkRed);
                Program.TwitchChecker?.MarkDownloadFinished(channel);
                return;
            }

            var session = new DownloadSession { Channel = channel, SessionCode = sessionCode };
            _activeSessions[channel] = session;

            var worker = new Thread(() => RunDownloadSession(session, hlsUrl, files))
            {
                IsBackground = true,
                Name = $"DL-{channel}-{sessionCode}"
            };
            worker.Start();
        }

        /// <summary>
        /// Возвращает имена каналов с активными в данный момент сессиями загрузки.
        /// </summary>
        public IReadOnlyList<string> GetActiveDownloads()
        {
            return _activeSessions.Keys.ToList();
        }

        /// <summary>
        /// Принудительно завершает все ffmpeg-процессы сессии указанного канала.
        /// Возвращает false если активной сессии нет.
        /// </summary>
        public bool StopDownload(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return false;
            if (!_activeSessions.TryGetValue(channel, out var session)) return false;

            session.ForcedByUser = true;
            List<Process> snapshot;
            lock (session.ProcessesLock)
            {
                snapshot = session.Processes.ToList();
            }
            foreach (var p in snapshot)
            {
                try { if (p != null && !p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            }
            return true;
        }

        private void RunDownloadSession(DownloadSession session, string hlsUrl, DownloadFiles files)
        {
            var channel = session.Channel;
            var sessionCode = session.SessionCode;
            bool abnormalTermination = false;

            try
            {
                // Если пользователь успел нажать "Завершить загрузку" до старта потока — не запускаем ffmpeg
                if (session.ForcedByUser)
                {
                    ConsoleWriteLine($"Сессия канала '{channel}' отменена пользователем до запуска ffmpeg");
                    return;
                }

                var started = new List<Process>();
                try
                {
                    started.Add(StartFfmpegProcess(hlsUrl, files.Video1, audioOnly: false));
                    started.Add(StartFfmpegProcess(hlsUrl, files.Video2, audioOnly: false));
                    started.Add(StartFfmpegProcess(hlsUrl, files.Audio1, audioOnly: true));
                    started.Add(StartFfmpegProcess(hlsUrl, files.Audio2, audioOnly: true));
                }
                catch (Exception ex)
                {
                    abnormalTermination = true;
                    ConsoleWriteLine($"Не удалось запустить все ffmpeg-процессы для канала '{channel}': {ex.Message}", ConsoleColor.DarkRed);
                    foreach (var p in started)
                    {
                        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                    }
                    return;
                }

                lock (session.ProcessesLock)
                {
                    session.Processes.AddRange(started);
                }

                // Если пользователь нажал Stop пока стартовали — убить только что запущенные
                if (session.ForcedByUser)
                {
                    foreach (var p in started)
                    {
                        try { if (p != null && !p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                    }
                }

                var waits = started.Where(p => p != null)
                    .Select(p => Task.Run(() => { try { p.WaitForExit(); } catch { } }))
                    .ToArray();

                if (waits.Length > 0)
                {
                    // 1. Ждём первого завершения
                    try { Task.WaitAny(waits); } catch { }

                    // 2. Даём остальным 10 секунд на самозакрытие
                    bool allFinished;
                    try { allFinished = Task.WaitAll(waits, TimeSpan.FromSeconds(10)); }
                    catch { allFinished = false; }

                    if (!allFinished)
                    {
                        // Если это пользовательский Stop — мы и так убили процессы, abnormal не выставляем
                        if (!session.ForcedByUser)
                        {
                            abnormalTermination = true;
                            ConsoleWriteLine($"Не все ffmpeg-процессы '{channel}' закрылись за 10с после первого. Принудительное завершение.", ConsoleColor.DarkYellow);
                        }
                        foreach (var p in started)
                        {
                            try { if (p != null && !p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                        }
                        try { Task.WaitAll(waits, TimeSpan.FromSeconds(5)); } catch { }
                    }
                }

                // Проверка хешей аудио и видео (только если не был форсированный стоп — в его случае файлы наверняка обрезаны и дедуп бесполезен)
                bool audioEqual = FilesEqualByHash(files.Audio1, files.Audio2);
                bool videoEqual = FilesEqualByHash(files.Video1, files.Video2);

                if (audioEqual)
                {
                    SafeDelete(files.Audio1);
                }
                if (videoEqual)
                {
                    SafeDelete(files.Video1);
                }
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка сессии загрузки канала '{channel}': {ex.Message}", ConsoleColor.DarkRed);
            }
            finally
            {
                _activeSessions.TryRemove(channel, out _);
                try { Program.TwitchChecker?.MarkDownloadFinished(channel); } catch { }
                ConsoleWriteLine($"Загрузка канала '{channel}' завершена (сессия {sessionCode})");

                try
                {
                    if (session.ForcedByUser)
                    {
                        _ = Program.TelegramServiceInstance.SendNotification($"⛔ Загрузка стрима <b>{channel}</b> принудительно приостановлена");
                    }
                    else if (abnormalTermination)
                    {
                        _ = Program.TelegramServiceInstance.SendNotification($"⚠️ При завершении загрузки <b>{channel}</b> один из ffmpeg-процессов был принудительно закрыт. Канал будет проверен заново.");
                        try { Program.TwitchChecker?.ForceCheck(); } catch { }
                    }
                    else
                    {
                        _ = Program.TelegramServiceInstance.SendNotification($"Загрузка стрима {channel} Завершена");
                    }
                }
                catch { }

                lock (session.ProcessesLock)
                {
                    foreach (var p in session.Processes) { try { p?.Dispose(); } catch { } }
                }
            }
        }

        private static bool FilesEqualByHash(string path1, string path2)
        {
            if (!File.Exists(path1) || !File.Exists(path2)) return false;
            using var sha = SHA256.Create();
            using var f1 = new FileStream(path1, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var f2 = new FileStream(path2, FileMode.Open, FileAccess.Read, FileShare.Read);
            var h1 = sha.ComputeHash(f1);
            var h2 = sha.ComputeHash(f2);
            return h1.AsSpan().SequenceEqual(h2);
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private string ResolveHlsUrl(string channel)
        {
            // yt-dlp --no-warnings --get-url https://www.twitch.tv/<channel>
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"--no-warnings --get-url https://www.twitch.tv/{channel}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                if (!proc.Start()) return string.Empty;
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    ConsoleWriteLine($"yt-dlp вернул код {proc.ExitCode}. {stderr}", ConsoleColor.DarkYellow);
                    return string.Empty;
                }

                // Первая строка с m3u8
                var line = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return line ?? string.Empty;
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка запуска yt-dlp: {ex.Message}", ConsoleColor.DarkRed);
                return string.Empty;
            }
        }

        private Process StartFfmpegProcess(string hlsUrl, string outputPath, bool audioOnly)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            var commonArguments = new[]
            {
                "-hide_banner",
                "-loglevel", "warning",
                "-nostdin",
                "-y",
                "-reconnect", "1",
                "-reconnect_streamed", "1",
                "-reconnect_at_eof", "1",
                "-reconnect_on_network_error", "1",
                "-reconnect_delay_max", "10",
                "-i", hlsUrl
            };

            foreach (var argument in commonArguments)
                psi.ArgumentList.Add(argument);

            if (audioOnly)
            {
                psi.ArgumentList.Add("-vn");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("aac");
                psi.ArgumentList.Add("-b:a");
                psi.ArgumentList.Add("160k");
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("adts");
            }
            else
            {
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("copy");
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("mpegts");
            }

            psi.ArgumentList.Add(outputPath);

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = false };
            try
            {
                if (!proc.Start())
                    throw new InvalidOperationException("Process.Start вернул false");
            }
            catch (Exception ex)
            {
                proc.Dispose();
                ConsoleWriteLine($"Ошибка запуска ffmpeg: {ex.Message}. Убедитесь, что ffmpeg доступен в PATH.", ConsoleColor.DarkRed);
                throw;
            }
            return proc;
        }

        private string GenerateCode(int len)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            Span<char> buf = stackalloc char[len];
            for (int i = 0; i < len; i++) buf[i] = alphabet[_rng.Next(alphabet.Length)];
            return new string(buf);
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
    }
}
