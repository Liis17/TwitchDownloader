using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class MediaFinalizerTests
{
    [Fact]
    public async Task FinalizeAsync_PublishesValidatedMp4AndDeletesRawRecording()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var rawPath = Path.Combine(temporaryDirectory.Path, "live_channel.recording");
        await File.WriteAllTextAsync(rawPath, "raw-media");
        var toolRunner = new FakeMediaToolRunner
        {
            ProbeOutput = """
                codec_type=video
                duration=10.000000
                codec_type=audio
                duration=10.020000
                duration=10.020000
                """
        };
        var finalizer = new MediaFinalizer(toolRunner);

        var result = await finalizer.FinalizeAsync(
            rawPath,
            expectedDurationSeconds: 10,
            RecordingContainer.MpegTs,
            CancellationToken.None);

        Assert.Equal(MediaFinalizeStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(temporaryDirectory.Path, "live_channel.mp4"), result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
        Assert.False(File.Exists(rawPath));
        Assert.False(File.Exists(result.OutputPath + ".part"));
    }

    [Fact]
    public async Task RecoverOrphansAsync_FinalizesOnlyNewRecordingFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var orphanPath = Path.Combine(temporaryDirectory.Path, "live_channel.recording");
        var legacyPath = Path.Combine(temporaryDirectory.Path, "channel_video_1_old.ts");
        await File.WriteAllTextAsync(orphanPath, "raw-media");
        await File.WriteAllTextAsync(legacyPath, "legacy-media");
        var toolRunner = new FakeMediaToolRunner
        {
            DecodeOutput = "frame=1 time=00:00:05.00 bitrate=0.0kbits/s",
            ProbeOutput = """
                codec_type=video
                duration=5.000000
                codec_type=audio
                duration=5.010000
                duration=5.010000
                """
        };
        var finalizer = new MediaFinalizer(toolRunner);

        var results = await finalizer.RecoverOrphansAsync(temporaryDirectory.Path, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(MediaFinalizeStatus.Succeeded, result.Status);
        Assert.True(File.Exists(Path.Combine(temporaryDirectory.Path, "live_channel.mp4")));
        Assert.True(File.Exists(legacyPath));
    }

    [Fact]
    public async Task FinalizeAsync_ParksRawAndCandidateWhenValidationFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var rawPath = Path.Combine(temporaryDirectory.Path, "live_channel.recording");
        await File.WriteAllTextAsync(rawPath, "raw-media");
        var toolRunner = new FakeMediaToolRunner
        {
            ProbeOutput = """
                codec_type=video
                duration=4.000000
                codec_type=audio
                duration=4.010000
                duration=4.010000
                """
        };
        var finalizer = new MediaFinalizer(toolRunner);

        var result = await finalizer.FinalizeAsync(
            rawPath,
            expectedDurationSeconds: 20,
            RecordingContainer.MpegTs,
            CancellationToken.None);

        Assert.Equal(MediaFinalizeStatus.Failed, result.Status);
        Assert.True(File.Exists(rawPath + ".failed"));
        Assert.True(File.Exists(Path.Combine(temporaryDirectory.Path, "live_channel.mp4.failed")));
        Assert.False(File.Exists(Path.Combine(temporaryDirectory.Path, "live_channel.mp4")));
    }

    [Fact]
    public async Task FinalizeAsync_ParksFilesWhenProbeThrows()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var rawPath = Path.Combine(temporaryDirectory.Path, "live_channel.recording");
        await File.WriteAllTextAsync(rawPath, "raw-media");
        var finalizer = new MediaFinalizer(new FakeMediaToolRunner { ThrowOnProbe = true });

        var result = await finalizer.FinalizeAsync(
            rawPath,
            expectedDurationSeconds: 10,
            RecordingContainer.MpegTs,
            CancellationToken.None);

        Assert.Equal(MediaFinalizeStatus.Failed, result.Status);
        Assert.True(File.Exists(rawPath + ".failed"));
        Assert.True(File.Exists(Path.Combine(temporaryDirectory.Path, "live_channel.mp4.failed")));
    }

    [Fact]
    public async Task FinalizeAsync_CreatesFailedArtifactsWhenFfmpegProducesNoCandidate()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var rawPath = Path.Combine(temporaryDirectory.Path, "live_channel.recording");
        await File.WriteAllTextAsync(rawPath, "raw-media");
        var finalizer = new MediaFinalizer(new FakeMediaToolRunner { FfmpegExitCode = 1 });

        var result = await finalizer.FinalizeAsync(
            rawPath,
            expectedDurationSeconds: 10,
            RecordingContainer.MpegTs,
            CancellationToken.None);

        Assert.Equal(MediaFinalizeStatus.Failed, result.Status);
        Assert.True(File.Exists(rawPath + ".failed"));
        Assert.True(File.Exists(Path.Combine(temporaryDirectory.Path, "live_channel.mp4.failed")));
    }

    private sealed class FakeMediaToolRunner : IMediaToolRunner
    {
        public string ProbeOutput { get; init; } = string.Empty;
        public string DecodeOutput { get; init; } = string.Empty;
        public bool ThrowOnProbe { get; init; }
        public int FfmpegExitCode { get; init; }

        public Task<MediaToolResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            if (executable == "ffprobe")
            {
                if (ThrowOnProbe)
                    throw new InvalidOperationException("ffprobe failed to start");
                return Task.FromResult(new MediaToolResult(0, ProbeOutput, string.Empty));
            }

            if (arguments[^1] == "-")
                return Task.FromResult(new MediaToolResult(0, string.Empty, DecodeOutput));

            if (FfmpegExitCode != 0)
                return Task.FromResult(new MediaToolResult(FfmpegExitCode, string.Empty, "encoding failed"));

            var outputPath = arguments[^1];
            File.WriteAllText(outputPath, "valid-mp4");
            return Task.FromResult(new MediaToolResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"twitch-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
