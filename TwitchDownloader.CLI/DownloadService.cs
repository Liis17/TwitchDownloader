using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class DownloadService
{
    private readonly string _savePath;
    private readonly ConcurrentDictionary<string, Process> _activeProcesses = new();
    private readonly ConcurrentDictionary<string, bool> _activeChannels = new();

    public DownloadService(string savePath = "")
    {
        _savePath = string.IsNullOrEmpty(savePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : savePath;

        if (!Directory.Exists(_savePath))
            Directory.CreateDirectory(_savePath);
    }

    public void DownloadStream(string m3u8Url, string channelName, bool withAudioOffset = false)
    {
        // Проверяем, не идет ли уже загрузка для этого канала
        if (_activeChannels.ContainsKey(channelName))
        {
            Console.WriteLine($"Загрузка для канала '{channelName}' уже выполняется.");
            return;
        }

        // Добавляем канал в активные загрузки
        _activeChannels[channelName] = true;

        var guid = Guid.NewGuid().ToString("N");
        StartFfmpegProcess(guid, channelName, m3u8Url, withAudioOffset);
    }

    private void StartFfmpegProcess(string guid, string channel, string m3u8Url, bool withAudioOffset)
    {
        try
        {
            string videoFileName = GenerateFileName(channel, "video", "mp4", guid);
            string videoArgs = $"-i \"{m3u8Url}\" -c copy \"{videoFileName}\"";

            StartProcess(guid, "video", videoArgs, channel, videoFileName);

            if (withAudioOffset)
            {
                string audio1FileName = GenerateFileName(channel, "audio1", "aac", guid);
                string audio2FileName = GenerateFileName(channel, "audio2", "aac", guid);

                string audio1Args = $"-i \"{m3u8Url}\" -vn -acodec copy \"{audio1FileName}\"";
                string audio2Args = $"-itsoffset 1 -i \"{m3u8Url}\" -vn -acodec copy \"{audio2FileName}\"";

                StartProcess($"{guid}_a1", "audio1", audio1Args, channel);
                Task.Delay(1000).ContinueWith(_ => StartProcess($"{guid}_a2", "audio2", audio2Args, channel));
            }
        }
        catch (Exception ex)
        {
            _activeChannels.TryRemove(channel, out _);
            Console.WriteLine($"Ошибка запуска FFmpeg: {ex.Message}");
        }
    }

    private string GenerateFileName(string channel, string type, string extension, string guid)
    {
        string safeChannelName = new string(channel
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c))
            .ToArray());

        return Path.Combine(_savePath, $"{safeChannelName}_{type}_{guid.Substring(0, 6)}.{extension}");
    }

    private void StartProcess(string guid, string type, string arguments, string channel = "", string fileName = "")
    {
        try
        {
            if (!string.IsNullOrEmpty(channel) && !string.IsNullOrEmpty(fileName))
            {
                Program.telegramService.NotifyDownloadStart(channel, fileName, guid.Substring(0, 6));
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                },
                EnableRaisingEvents = true
            };

            process.Exited += (sender, e) =>
            {
                _activeProcesses.TryRemove(guid, out _);
                Console.WriteLine($"[FFmpeg {type}] Процесс завершен с кодом {process.ExitCode}");
                process.Dispose();

                // Проверяем, остались ли активные процессы для этого канала
                if (!_activeProcesses.Values.Any(p =>
                    !p.HasExited &&
                    p.StartInfo.Arguments.Contains($"\"{channel}_")))
                {
                    _activeChannels.TryRemove(channel, out _);
                    if (!string.IsNullOrEmpty(channel) && !string.IsNullOrEmpty(fileName))
                    {
                        Program.telegramService.NotifyDownloadComplete(channel, fileName, guid.Substring(0, 6));
                    }
                }
            };

            if (_activeProcesses.TryAdd(guid, process))
            {
                process.Start();
                Console.WriteLine($"[FFmpeg {type}] Запущен процесс {guid}");
            }
            else
            {
                Console.WriteLine($"[FFmpeg {type}] Не удалось добавить процесс {guid}");
            }
        }
        catch (Exception ex)
        {
            _activeChannels.TryRemove(channel, out _);
            Console.WriteLine($"[FFmpeg {type}] Ошибка запуска процесса: {ex.Message}");
        }
    }

    public bool IsDownloading(string channel)
    {
        return _activeChannels.ContainsKey(channel);
    }

    public void StopAllDownloads()
    {
        foreach (var process in _activeProcesses.Values.ToList())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    Console.WriteLine($"Остановлен процесс {process.Id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при остановке процесса: {ex.Message}");
            }
        }

        _activeProcesses.Clear();
        _activeChannels.Clear();
    }
}