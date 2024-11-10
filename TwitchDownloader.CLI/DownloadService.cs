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
        public void StartDownload(string link)
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

            DownloadStream(m3u8Url);
        }

        public void StartAutoDownload(string link, string channelName)
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
            DownloadStream(m3u8Url, channelName);
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

        private void DownloadStream(string m3u8Url, string filename = "twitch")
        {
            string SavePath = savePath;
            if (savePath == string.Empty)
            {
                SavePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            Guid guid = Guid.NewGuid();
            string outputFileName = Path.Combine(SavePath, $"{filename}_{DateTime.Now.Hour}_{DateTime.Now.Minute}_{DateTime.Now.Second}_{guid}.mp4");  // Или ".mkv" если нужно

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

            Console.WriteLine($"Видео сохранено на рабочий стол как {outputFileName}");
            Program.telegramService.SendMessage($"Видео сохранено на рабочий стол как {outputFileName}");
        }
    }
}
