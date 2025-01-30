using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

public class ConverterService
{
    public async Task ConvertAndMergeAsync(string videoPath, string audioPath)
    {
        try
        {
            // Создаем папку temp, если её нет
            string tempFolder = Path.Combine(Path.GetDirectoryName(videoPath), "temp");
            Directory.CreateDirectory(tempFolder);

            // Генерация путей для временных файлов
            string mp3Path = Path.Combine(tempFolder, Path.GetFileNameWithoutExtension(audioPath) + ".mp3");
            string silentVideoPath = Path.Combine(tempFolder, Path.GetFileNameWithoutExtension(videoPath) + "_silent.mp4");
            string outputPath = Path.Combine(Path.GetDirectoryName(videoPath),
                Path.GetFileNameWithoutExtension(videoPath) + "_final.mp4");

            // Конвертация AAC в MP3
            await RunFfmpegProcess($"-i \"{audioPath}\" -codec:a libmp3lame -qscale:a 2 \"{mp3Path}\"");

            // Удаление звука из видео
            await RunFfmpegProcess($"-i \"{videoPath}\" -c:v copy -an \"{silentVideoPath}\"");

            // Слияние видео и аудио
            await RunFfmpegProcess($"-i \"{silentVideoPath}\" -i \"{mp3Path}\" -c:v copy -c:a aac -map 0:v:0 -map 1:a:0 \"{outputPath}\"");

            Console.WriteLine($"Конвертация завершена. Результат: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при конвертации: {ex.Message}");
        }
    }

    private Task RunFfmpegProcess(string arguments)
    {
        return Task.Run(() =>
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Console.WriteLine($"Запуск FFmpeg с аргументами: {arguments}");
                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"FFmpeg завершился с кодом ошибки: {process.ExitCode}");
                }
            }
        });
    }
}