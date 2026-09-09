using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TwitchDownloader2.CLI
{
    internal enum MediaFinalizeStatus
    {
        Succeeded,
        Failed,
        NoContent
    }

    internal sealed record MediaFinalizeResult(
        MediaFinalizeStatus Status,
        string RawPath,
        string OutputPath,
        double ExpectedDurationSeconds,
        double VideoDurationSeconds,
        double AudioDurationSeconds,
        string? ErrorMessage);

    internal sealed class MediaFinalizer
    {
        private const double DurationToleranceSeconds = 2;
        private readonly IMediaToolRunner _toolRunner;
        private readonly Action<string>? _log;

        public MediaFinalizer(IMediaToolRunner toolRunner, Action<string>? log = null)
        {
            _toolRunner = toolRunner;
            _log = log;
        }

        public async Task<MediaFinalizeResult> FinalizeAsync(
            string rawPath,
            double expectedDurationSeconds,
            RecordingContainer container,
            CancellationToken cancellationToken)
        {
            var outputPath = GetOutputPath(rawPath);
            var partPath = outputPath + ".part";

            if (!File.Exists(rawPath) || new FileInfo(rawPath).Length == 0)
            {
                TryDelete(rawPath);
                return new MediaFinalizeResult(
                    MediaFinalizeStatus.NoContent,
                    rawPath,
                    outputPath,
                    expectedDurationSeconds,
                    0,
                    0,
                    "The raw recording is empty.");
            }

            TryDelete(partPath);
            var ffmpegArguments = new[]
            {
                "-hide_banner",
                "-loglevel", "warning",
                "-nostdin",
                "-y",
                "-f", GetFfmpegFormat(container),
                "-i", rawPath,
                "-c:v", "copy",
                "-c:a", "aac",
                "-b:a", "160k",
                "-ar", "48000",
                "-ac", "2",
                "-af", "aresample=async=1:first_pts=0",
                "-f", "mp4",
                partPath
            };

            MediaToolResult ffmpegResult;
            try
            {
                ffmpegResult = await _toolRunner.RunAsync("ffmpeg", ffmpegArguments, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ParkFailedFiles(rawPath, partPath, outputPath, expectedDurationSeconds, 0, 0, ex.Message);
            }

            if (ffmpegResult.ExitCode != 0 || !File.Exists(partPath))
            {
                var message = $"ffmpeg exited with code {ffmpegResult.ExitCode}: {TrimForLog(ffmpegResult.StandardError)}";
                return ParkFailedFiles(rawPath, partPath, outputPath, expectedDurationSeconds, 0, 0, message);
            }

            var hasTimestampErrors = ffmpegResult.StandardError.Contains(
                "Non-monotonous DTS",
                StringComparison.OrdinalIgnoreCase);
            (double VideoSeconds, double AudioSeconds) probe;
            try
            {
                probe = await ProbeAsync(partPath, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ParkFailedFiles(rawPath, partPath, outputPath, expectedDurationSeconds, 0, 0, ex.Message);
            }
            var validDuration = expectedDurationSeconds > 0
                && Math.Abs(probe.VideoSeconds - expectedDurationSeconds) <= DurationToleranceSeconds;
            var validSync = probe.VideoSeconds > 0
                && probe.AudioSeconds > 0
                && Math.Abs(probe.AudioSeconds - probe.VideoSeconds) <= DurationToleranceSeconds;

            if (!validDuration || !validSync || hasTimestampErrors)
            {
                var message = $"Validation failed: video={probe.VideoSeconds:F2}s, audio={probe.AudioSeconds:F2}s, expected={expectedDurationSeconds:F2}s, non-monotonous DTS={hasTimestampErrors}.";
                return ParkFailedFiles(
                    rawPath,
                    partPath,
                    outputPath,
                    expectedDurationSeconds,
                    probe.VideoSeconds,
                    probe.AudioSeconds,
                    message);
            }

            try
            {
                File.Move(partPath, outputPath, overwrite: false);
            }
            catch (Exception ex)
            {
                return ParkFailedFiles(
                    rawPath,
                    partPath,
                    outputPath,
                    expectedDurationSeconds,
                    probe.VideoSeconds,
                    probe.AudioSeconds,
                    $"Could not publish validated MP4: {ex.Message}");
            }

            TryDelete(rawPath);
            _log?.Invoke($"Finalized recording: {outputPath}");
            return new MediaFinalizeResult(
                MediaFinalizeStatus.Succeeded,
                rawPath,
                outputPath,
                expectedDurationSeconds,
                probe.VideoSeconds,
                probe.AudioSeconds,
                null);
        }

        public async Task<IReadOnlyList<MediaFinalizeResult>> RecoverOrphansAsync(
            string directory,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(directory))
                return Array.Empty<MediaFinalizeResult>();

            var orphanPaths = Directory
                .EnumerateFiles(directory, "*.recording", SearchOption.TopDirectoryOnly)
                .ToArray();
            var results = new List<MediaFinalizeResult>(orphanPaths.Length);
            foreach (var rawPath in orphanPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var container = DetectContainer(rawPath);
                    var expectedDuration = await DecodeDurationAsync(rawPath, container, cancellationToken);
                    results.Add(await FinalizeAsync(rawPath, expectedDuration, container, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var outputPath = GetOutputPath(rawPath);
                    results.Add(ParkFailedFiles(
                        rawPath,
                        outputPath + ".part",
                        outputPath,
                        0,
                        0,
                        0,
                        $"Recovery failed: {ex.Message}"));
                }
            }

            return results;
        }

        private async Task<double> DecodeDurationAsync(
            string path,
            RecordingContainer container,
            CancellationToken cancellationToken)
        {
            var arguments = new[]
            {
                "-v", "error",
                "-stats",
                "-nostdin",
                "-f", GetFfmpegFormat(container),
                "-i", path,
                "-f", "null",
                "-"
            };
            var result = await _toolRunner.RunAsync("ffmpeg", arguments, cancellationToken);
            if (result.ExitCode != 0)
                return 0;

            var matches = Regex.Matches(
                result.StandardError,
                "time=(\\d{2}):(\\d{2}):(\\d{2}(?:\\.\\d+)?)",
                RegexOptions.CultureInvariant);
            if (matches.Count == 0)
                return 0;

            var match = matches[^1];
            var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            return hours * 3600 + minutes * 60 + seconds;
        }

        private async Task<(double VideoSeconds, double AudioSeconds)> ProbeAsync(
            string path,
            CancellationToken cancellationToken)
        {
            var arguments = new[]
            {
                "-v", "error",
                "-show_entries", "stream=codec_type,duration",
                "-show_entries", "format=duration",
                "-of", "default=nw=1",
                path
            };
            var result = await _toolRunner.RunAsync("ffprobe", arguments, cancellationToken);
            if (result.ExitCode != 0)
                return (0, 0);

            var video = 0d;
            var audio = 0d;
            string currentType = string.Empty;
            foreach (var rawLine in result.StandardOutput.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("codec_type=", StringComparison.Ordinal))
                {
                    currentType = line["codec_type=".Length..];
                    continue;
                }

                if (!line.StartsWith("duration=", StringComparison.Ordinal)
                    || !double.TryParse(
                        line["duration=".Length..],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var duration))
                {
                    continue;
                }

                if (currentType == "video" && video == 0)
                    video = duration;
                else if (currentType == "audio" && audio == 0)
                    audio = duration;
            }

            return (video, audio);
        }

        private MediaFinalizeResult ParkFailedFiles(
            string rawPath,
            string partPath,
            string outputPath,
            double expectedDurationSeconds,
            double videoDurationSeconds,
            double audioDurationSeconds,
            string message)
        {
            var failedRawPath = rawPath + ".failed";
            var failedOutputPath = outputPath + ".failed";
            TryMove(rawPath, failedRawPath);
            TryMove(partPath, failedOutputPath);
            _log?.Invoke(message);
            return new MediaFinalizeResult(
                MediaFinalizeStatus.Failed,
                failedRawPath,
                failedOutputPath,
                expectedDurationSeconds,
                videoDurationSeconds,
                audioDurationSeconds,
                message);
        }

        private static string GetOutputPath(string rawPath)
        {
            const string suffix = ".recording";
            return rawPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? rawPath[..^suffix.Length] + ".mp4"
                : rawPath + ".mp4";
        }

        private static string GetFfmpegFormat(RecordingContainer container)
        {
            return container == RecordingContainer.FragmentedMp4 ? "mp4" : "mpegts";
        }

        private static RecordingContainer DetectContainer(string path)
        {
            try
            {
                Span<byte> header = stackalloc byte[12];
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var read = stream.Read(header);
                if (read > 0 && header[0] == 0x47)
                    return RecordingContainer.MpegTs;

                if (read >= 8)
                {
                    var box = Encoding.Latin1.GetString(header[4..8]);
                    if (box is "ftyp" or "styp" or "moof" or "moov" or "emsg" or "free" or "skip")
                        return RecordingContainer.FragmentedMp4;
                }
            }
            catch
            {
                // The finalizer will preserve unreadable input as a failed recording.
            }

            return RecordingContainer.MpegTs;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A stale part file is handled by the following ffmpeg/file operation.
            }
        }

        private static void TryMove(string source, string destination)
        {
            try
            {
                if (File.Exists(source) && !File.Exists(destination))
                    File.Move(source, destination);
            }
            catch
            {
                // Preserve the original file when it cannot be parked.
            }
        }

        private static string TrimForLog(string text)
        {
            var compact = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return compact.Length <= 400 ? compact : compact[..400];
        }
    }
}
