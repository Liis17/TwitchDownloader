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

        public void StartDownload(string channelName)
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
            Program.TelegramServiceInstance.SendNotification(message);
            Thread.Sleep(1000);
            var path = Program.Settings.DownloadPath;
            var message2 = $"" +
                $"📂 <b>Файлы этой транцляции:</b>\n" +
                $"<pre>🎞️ {path}\\{channel}_video_1_{sessionCode}.ts\n" +
                $"🎞️ {path}\\{channel}_video_2_{sessionCode}.ts\n" +
                $"🎵 {path}\\{channel}_audio_1_{sessionCode}.aac\n" +
                $"🎵 {path}\\{channel}_audio_2_{sessionCode}.aac</pre>\n\n" +
                $"<b>После успешной загрузки будет перекодирован в:</b>\n" +
                $"<pre>🎞️ {path}\\{channel}_video_2_{sessionCode}_final.mp4</pre>";

            Program.TelegramServiceInstance.SendNotification(message2);

            var hlsUrl = ResolveHlsUrl(channel);
            if (string.IsNullOrWhiteSpace(hlsUrl))
            {
                ConsoleWriteLine("Не удалось получить HLS URL через yt-dlp", ConsoleColor.DarkRed);
                Program.TwitchChecker?.MarkDownloadFinished(channel);
                return;
            }

            string fileVideo1 = Path.Combine(_downloadRoot, $"{channel}_video_1_{sessionCode}.ts");
            string fileVideo2 = Path.Combine(_downloadRoot, $"{channel}_video_2_{sessionCode}.ts");
            string fileAudio1 = Path.Combine(_downloadRoot, $"{channel}_audio_1_{sessionCode}.aac");
            string fileAudio2 = Path.Combine(_downloadRoot, $"{channel}_audio_2_{sessionCode}.aac");

            var worker = new Thread(() => RunDownloadSession(channel, sessionCode, hlsUrl, fileVideo1, fileVideo2, fileAudio1, fileAudio2))
            {
                IsBackground = true,
                Name = $"DL-{channel}-{sessionCode}"
            };
            worker.Start();
        }

        private void RunDownloadSession(string channel, string sessionCode, string hlsUrl, string fileVideo1, string fileVideo2, string fileAudio1, string fileAudio2)
        {
            var processes = new List<Process>();
            try
            {
                string common = "-hide_banner -loglevel warning -y -reconnect 1 -reconnect_streamed 1 -reconnect_at_eof 1 -reconnect_on_network_error 1 -reconnect_delay_max 10";

                processes.Add(StartFfmpegInNewWindow($"[TD2] {channel} video #1", $"{common} -i \"{hlsUrl}\" -c copy -f mpegts \"{fileVideo1}\""));
                processes.Add(StartFfmpegInNewWindow($"[TD2] {channel} video #2", $"{common} -i \"{hlsUrl}\" -c copy -f mpegts \"{fileVideo2}\""));
                processes.Add(StartFfmpegInNewWindow($"[TD2] {channel} audio #1", $"{common} -i \"{hlsUrl}\" -vn -c:a aac -b:a 160k -f adts \"{fileAudio1}\""));
                processes.Add(StartFfmpegInNewWindow($"[TD2] {channel} audio #2", $"{common} -i \"{hlsUrl}\" -vn -c:a aac -b:a 160k -f adts \"{fileAudio2}\""));

                var waits = processes.Where(p => p != null).Select(p => Task.Run(() => { try { p.WaitForExit(); } catch { } })).ToArray();
                try { Task.WaitAll(waits); } catch { }

                // Проверка хешей аудио и видео
                bool audioEqual = FilesEqualByHash(fileAudio1, fileAudio2);
                bool videoEqual = FilesEqualByHash(fileVideo1, fileVideo2);

                string outputFile;
                if (audioEqual && videoEqual)
                {
                    // Идентичные: удаляем аудио1/видео1 и собираем из пары #2; затем чистим всё
                    SafeDelete(fileAudio1);
                    SafeDelete(fileVideo1);

                    var outDir = Path.GetDirectoryName(fileVideo2) ?? _downloadRoot;
                    var tempDir = Path.Combine(outDir, $"temp_{sessionCode}");
                    Directory.CreateDirectory(tempDir);

                    var tempAudioMp3 = Path.Combine(tempDir, "audio.mp3");
                    RunFfmpegBlocking($"-i \"{fileAudio2}\" -q:a 0 -map a \"{tempAudioMp3}\"", "Конвертация аудио в mp3 завершена.");

                    var tempVideoNoAudio = Path.Combine(tempDir, "video_no_audio.mp4");
                    RunFfmpegBlocking($"-i \"{fileVideo2}\" -an -vcodec copy \"{tempVideoNoAudio}\"", "Видео без аудио подготовлено.");

                    var baseName = Path.GetFileNameWithoutExtension(fileVideo2);
                    outputFile = Path.Combine(outDir, $"{baseName}_final.mp4");
                    RunFfmpegBlocking($"-i \"{tempVideoNoAudio}\" -i \"{tempAudioMp3}\" -c:v copy -c:a aac -strict experimental \"{outputFile}\"", "Финальный файл собран.");

                    // Удаляем временную папку
                    try { Directory.Delete(tempDir, true); } catch { }

                    // Удаляем оставшиеся исходники (#2)
                    SafeDelete(fileAudio2);
                    SafeDelete(fileVideo2);
                }
                else
                {
                    // Разные: берём самые большие файлы по размеру, собираем, не удаляем исходники, а после перенесём их в папку sessionCode
                    string bestAudio = PickLargest(fileAudio1, fileAudio2);
                    string bestVideo = PickLargest(fileVideo1, fileVideo2);

                    var outDir = Path.GetDirectoryName(bestVideo) ?? _downloadRoot;
                    var tempDir = Path.Combine(outDir, $"temp_{sessionCode}");
                    Directory.CreateDirectory(tempDir);

                    var tempAudioMp3 = Path.Combine(tempDir, "audio.mp3");
                    RunFfmpegBlocking($"-i \"{bestAudio}\" -q:a 0 -map a \"{tempAudioMp3}\"", "Конвертация аудио в mp3 завершена.");

                    var tempVideoNoAudio = Path.Combine(tempDir, "video_no_audio.mp4");
                    RunFfmpegBlocking($"-i \"{bestVideo}\" -an -vcodec copy \"{tempVideoNoAudio}\"", "Видео без аудио подготовлено.");

                    var baseName = Path.GetFileNameWithoutExtension(bestVideo);
                    outputFile = Path.Combine(outDir, $"{baseName}_final.mp4");
                    RunFfmpegBlocking($"-i \"{tempVideoNoAudio}\" -i \"{tempAudioMp3}\" -c:v copy -c:a aac -strict experimental \"{outputFile}\"", "Финальный файл собран.");

                    // Удаляем временную папку
                    try { Directory.Delete(tempDir, true); } catch { }

                    // Переносим все исходники в папку sessionCode
                    var sessionDir = Path.Combine(_downloadRoot, sessionCode);
                    try { Directory.CreateDirectory(sessionDir); } catch { }
                    MoveIfExists(fileAudio1, Path.Combine(sessionDir, Path.GetFileName(fileAudio1)));
                    MoveIfExists(fileAudio2, Path.Combine(sessionDir, Path.GetFileName(fileAudio2)));
                    MoveIfExists(fileVideo1, Path.Combine(sessionDir, Path.GetFileName(fileVideo1)));
                    MoveIfExists(fileVideo2, Path.Combine(sessionDir, Path.GetFileName(fileVideo2)));
                }

                // Проверяем целостность выходного файла блоками по 4Кб
                VerifyFileReadable(outputFile, 4096);

                // Выброс исключения с путем к финальному файлу
                throw new Exception(outputFile);
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка сессии загрузки канала '{channel}': {ex.Message}", ConsoleColor.DarkRed);
            }
            finally
            {
                try { Program.TwitchChecker?.MarkDownloadFinished(channel); } catch { }
                foreach (var p in processes) { try { p?.Dispose(); } catch { } }
            }
        }

        private static string PickLargest(string path1, string path2)
        {
            long s1 = SafeLength(path1);
            long s2 = SafeLength(path2);
            return s2 > s1 ? path2 : path1;
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; } catch { return -1; }
        }

        private static void VerifyFileReadable(string path, int blockSize)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[blockSize];
            int read;
            long total = 0;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0) total += read;
            if (total == 0) throw new IOException("Пустой файл результата");
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

        private static void MoveIfExists(string src, string dst)
        {
            try { if (File.Exists(src)) File.Move(src, dst, overwrite: true); } catch { }
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

        private void RunFfmpegBlocking(string args, string successLog)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = new Process { StartInfo = psi };
            p.Start();
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                throw new Exception($"ffmpeg exit {p.ExitCode}: {stderr}");
            }
            else
            {
                ConsoleWriteLine(successLog);
            }
        }

        private Process StartFfmpegInNewWindow(string title, string ffmpegArgs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"{title}\" /WAIT ffmpeg {ffmpegArgs}",
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = false };
            try { proc.Start(); }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка запуска ffmpeg: {ex.Message}", ConsoleColor.DarkRed);
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
