using System;
using System.Diagnostics;

using Telegram.Bot.Types;

namespace TwitchDownloader.CLI
{
    class DownloadService
    {
        private bool _trackableRecording = false;
        private string savePath = string.Empty;

        public DownloadService(string path)
        {
            savePath = path;
        }
        public void StartDownload(string link, string downloader)
        {
            if (link.Length == 0)
            {
                Console.WriteLine("Укажите ссылку на страницу видео Twitch в качестве параметра запуска.");
                return;
            }

            string videoUrl = link;
            string m3u8Url = GetM3u8Url(videoUrl);

            if (string.IsNullOrEmpty(m3u8Url))
            {
                Console.WriteLine("Не удалось получить .m3u8 ссылку.");
                Program.telegramService.SendMessage($"Не удалось получить .m3u8 ссылку.");
                return;
            }

            switch (downloader)
            {
                case "defaultffmpeg":
                    DownloadDefaultFFMPEG(m3u8Url);
                    break;
                case "ffmpegrw_timeout":
                    Downloadffmpegrw_timeout(m3u8Url);
                    break;
                case "ffmpegbuffer":
                    Downloadffmpegbuffer(m3u8Url);
                    break;
                case "ffmpegwallclock":
                    Downloadffmpegwallclock(m3u8Url);
                    break;
                case "ytdlp":
                    Downloadytdlp(link);
                    break;
            }
        }

        public void StartAutoDownload(string link, string downloader, string channelName)
        {

            if (link.Length == 0)
            {
                Console.WriteLine("Укажите ссылку на страницу видео Twitch в качестве параметра запуска.");
                return;
            }
            if (_trackableRecording)
            {
                return;
            }
            string videoUrl = link;
            string m3u8Url = GetM3u8Url(videoUrl);

            if (string.IsNullOrEmpty(m3u8Url))
            {
                Console.WriteLine("На отслеживаемом канале не идет трансляция");
                return;
            }
            Program.telegramService.SendMessage($"Обнаружена трансляция на канале {channelName}, начинается скачивание...");
            _trackableRecording = true;
            switch (downloader)
            {
                case "defaultffmpeg":
                    DownloadDefaultFFMPEG(m3u8Url, channelName);
                    break;
                case "ffmpegrw_timeout":
                    Downloadffmpegrw_timeout(m3u8Url, channelName);
                    break;
                case "ffmpegbuffer":
                    Downloadffmpegbuffer(m3u8Url, channelName);
                    break;
                case "ffmpegwallclock":
                    Downloadffmpegwallclock(m3u8Url, channelName);
                    break;
                case "ytdlp":
                    Downloadytdlp(link, channelName);
                    break;
            }
            _trackableRecording = false;
            Program.telegramService.SendMessage($"Трансляция {channelName} завершилась и была сохранена");
        }

        private string GetM3u8Url(string videoUrl)
        {
            string m3u8Url = null;
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"-g \"{videoUrl}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = processInfo })
            {
                process.Start();
                m3u8Url = process.StandardOutput.ReadLine();
                process.WaitForExit();
            }

            return m3u8Url;
        }

        // теперь используется другой формат имени [twitch или имя канала]_[способ загрузки]_[дата и время]_[guid сокращенный до 4 символов].[формат (по умолчанию .mp4)]
        private void DownloadDefaultFFMPEG(string m3u8Url, string filename = "twitch")
        {
            string SavePath = savePath;
            if (savePath == string.Empty)
            {
                SavePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            Guid guid = Guid.NewGuid();
            string outputFileName = Path.Combine(SavePath, $"{filename}_ffmpeg_{DateTime.Now.Hour}-{DateTime.Now.Minute}-{DateTime.Now.Second}_{guid.ToString().Substring(1, 4)}.mp4");  // Или ".mkv" если нужно

            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{m3u8Url}\" -c copy \"{outputFileName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = processInfo })
            {
                process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                process.ErrorDataReceived += (sender, e) => Console.WriteLine(e.Data);

                Console.WriteLine("Начинаю загрузку...");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }

            Console.WriteLine($"Видео сохранено как {outputFileName}");
            Program.telegramService.SendMessage($"Видео сохранено как \n\n{outputFileName}");
        }

        private void Downloadffmpegrw_timeout(string m3u8Url, string filename = "twitch")
        {
            string SavePath = savePath;
            if (savePath == string.Empty)
            {
                SavePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            Guid guid = Guid.NewGuid();
            string outputFileName = Path.Combine(SavePath, $"{filename}_ffmpegrw_timeout_{DateTime.Now.Hour}-{DateTime.Now.Minute}-{DateTime.Now.Second}_{guid.ToString().Substring(1, 4)}.mp4");  // Или ".mkv" если нужно

            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-rw_timeout 10000000 -i \"{m3u8Url}\" -c copy \"{outputFileName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = processInfo })
            {
                process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                process.ErrorDataReceived += (sender, e) => Console.WriteLine(e.Data);

                Console.WriteLine("Начинаю загрузку...");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }

            Console.WriteLine($"Видео сохранено как {outputFileName}");
            Program.telegramService.SendMessage($"Видео сохранено как \n\n{outputFileName}");
        }

        private void Downloadffmpegbuffer(string m3u8Url, string filename = "twitch")
        {
            string SavePath = savePath;
            if (savePath == string.Empty)
            {
                SavePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            Guid guid = Guid.NewGuid();
            string outputFileName = Path.Combine(SavePath, $"{filename}_ffmpegbuffer_{DateTime.Now.Hour}-{DateTime.Now.Minute}-{DateTime.Now.Second}_{guid.ToString().Substring(1, 4)}.mp4");  // Или ".mkv" если нужно

            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-fflags +discardcorrupt -i \"{m3u8Url}\" -c copy \"{outputFileName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = processInfo })
            {
                process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                process.ErrorDataReceived += (sender, e) => Console.WriteLine(e.Data);

                Console.WriteLine("Начинаю загрузку...");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }

            Console.WriteLine($"Видео сохранено как {outputFileName}");
            Program.telegramService.SendMessage($"Видео сохранено как \n\n{outputFileName}");



        }

        private void Downloadffmpegwallclock(string m3u8Url, string filename = "twitch")
        {
            string SavePath = savePath;
            if (savePath == string.Empty)
            {
                SavePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            Guid guid = Guid.NewGuid();
            string outputFileName = Path.Combine(SavePath, $"{filename}_ffmpegwallclock_{DateTime.Now.Hour}-{DateTime.Now.Minute}-{DateTime.Now.Second}_{guid.ToString().Substring(1, 4)}.mp4");  // Или ".mkv" если нужно

            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-use_wallclock_as_timestamps 1 -re -i \"{m3u8Url}\" -c copy \"{outputFileName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = processInfo })
            {
                process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                process.ErrorDataReceived += (sender, e) => Console.WriteLine(e.Data);

                Console.WriteLine("Начинаю загрузку...");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }

            Console.WriteLine($"Видео сохранено как {outputFileName}");
            Program.telegramService.SendMessage($"Видео сохранено как \n\n{outputFileName}");
        }

        private void Downloadytdlp(string link, string filename = "twitch")
        {
            string SavePath = savePath;
            if (savePath == string.Empty)
            {
                SavePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            Guid guid = Guid.NewGuid();
            string outputFileName = Path.Combine(SavePath, $"{filename}_ytdlp_{DateTime.Now.Hour}-{DateTime.Now.Minute}-{DateTime.Now.Second}_{guid.ToString().Substring(1, 4)}.mp4");  // Или ".mkv" если нужно

            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"{link} -o \"{outputFileName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = processInfo })
            {
                process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                process.ErrorDataReceived += (sender, e) => Console.WriteLine(e.Data);

                Console.WriteLine("Начинается загрузка через yt-dlp");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }

            Console.WriteLine($"Видео сохранено как {outputFileName}");
            Program.telegramService.SendMessage($"Видео сохранено как \n\n{outputFileName}");
        }
    }
}
