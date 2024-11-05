using System.Diagnostics;

namespace TwitchDownloader.CLI
{
    class Program
    {
        static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Укажите ссылку на .m3u8 поток в качестве параметра запуска.");
            return;
        }

        string m3u8Url = args[0];
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string outputFileName = Path.Combine(desktopPath, "twitch_stream.mp4");  // Или ".mkv" если нужно в этом формате

        string ffmpegPath = "ffmpeg"; // Предполагаем, что ffmpeg находится в PATH
        string ffmpegArgs = $"-i \"{m3u8Url}\" -c copy \"{outputFileName}\"";

        ProcessStartInfo processInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = ffmpegArgs,
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
