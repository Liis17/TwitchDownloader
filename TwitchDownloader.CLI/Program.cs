using System.Diagnostics;

namespace TwitchDownloader.CLI
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Укажите ссылку на страницу видео Twitch в качестве параметра запуска.");
                return;
            }

            string videoUrl = args[0];
            string m3u8Url = GetM3u8Url(videoUrl);

            if (string.IsNullOrEmpty(m3u8Url))
            {
                Console.WriteLine("Не удалось получить .m3u8 ссылку.");
                return;
            }

            DownloadStream(m3u8Url);
        }

        private static string GetM3u8Url(string videoUrl)
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

        private static void DownloadStream(string m3u8Url)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            Guid guid = Guid.NewGuid();
            string outputFileName = Path.Combine(desktopPath, $"twitch_stream_{guid}.mp4");  // Или ".mkv" если нужно

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
        }
    }
}
