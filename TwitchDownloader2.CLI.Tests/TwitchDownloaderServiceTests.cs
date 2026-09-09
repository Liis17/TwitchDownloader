using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class TwitchDownloaderServiceTests
{
    [Fact]
    public async Task TryStartDownloadAsync_AllowsOneSessionPerChannelAndParallelChannels()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settings = CreateSettings(temporaryDirectory.Path);
        var playback = new ControlledPlaybackClient();
        playback.EnqueueLive("alpha");
        playback.EnqueueLive("beta");
        var service = CreateService(settings, playback);

        var alpha = await service.TryStartDownloadAsync("alpha", CancellationToken.None);
        var duplicateAlpha = await service.TryStartDownloadAsync("ALPHA", CancellationToken.None);
        var beta = await service.TryStartDownloadAsync("beta", CancellationToken.None);

        Assert.Equal(StartDownloadResult.Started, alpha);
        Assert.Equal(StartDownloadResult.AlreadyActive, duplicateAlpha);
        Assert.Equal(StartDownloadResult.Started, beta);
        Assert.Equal(2, service.GetActiveDownloads().Count);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StopForShutdownAsync(timeout.Token);
    }

    [Fact]
    public async Task RequestStopAsync_ReturnsBeforeFinalizationAndPublishesPartialMp4()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settings = CreateSettings(temporaryDirectory.Path);
        var playback = new ControlledPlaybackClient();
        playback.EnqueueLive("alpha");
        playback.EnqueuePlaylist("alpha", """
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXT-X-MEDIA-SEQUENCE:1
            #EXTINF:2,live
            content.ts
            """);
        playback.Segments[new Uri("https://video.example/alpha/content.ts")] = Encoding.UTF8.GetBytes("media");
        var mediaTools = new ControlledMediaToolRunner { BlockFinalization = true, DurationSeconds = 2 };
        var notifications = new RecordingNotifications();
        var saveCount = 0;
        var service = CreateService(settings, playback, mediaTools, notifications, () => saveCount++);

        Assert.Equal(
            StartDownloadResult.Started,
            await service.TryStartDownloadAsync("alpha", CancellationToken.None));
        var session = Assert.Single(service.GetActiveDownloads());
        await playback.SegmentFetched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(20);

        var stopResult = await service.RequestStopAsync(session.SessionId, CancellationToken.None);

        Assert.Equal(StopDownloadResult.Accepted, stopResult);
        Assert.Contains(settings.PausedUntilOfflineChannels, channel => channel == "alpha");
        Assert.True(saveCount > 0);
        await mediaTools.FinalizationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            StopDownloadResult.AlreadyStopping,
            await service.RequestStopAsync(session.SessionId, CancellationToken.None));
        Assert.Equal(ActiveDownloadState.Finalizing, Assert.Single(service.GetActiveDownloads()).State);

        mediaTools.ReleaseFinalization.TrySetResult();
        var completion = await notifications.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(completion.Succeeded, completion.ErrorMessage);
        Assert.True(File.Exists(completion.OutputPath));
        Assert.Equal(2, completion.DurationSeconds);
        Assert.Empty(service.GetActiveDownloads());
    }

    [Fact]
    public async Task SuppressionSurvivesErrorsAndIsRemovedOnlyByConfirmedOffline()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settings = CreateSettings(temporaryDirectory.Path);
        settings.PausedUntilOfflineChannels.Add("alpha");
        var playback = new ControlledPlaybackClient();
        playback.EnqueueResolution("alpha", new HttpRequestException("network unavailable"));
        playback.EnqueueOffline("alpha");
        var saveCount = 0;
        var service = CreateService(settings, playback, saveSettings: () => saveCount++);

        Assert.Equal(
            StartDownloadResult.Failed,
            await service.TryStartDownloadAsync("alpha", CancellationToken.None));
        Assert.Contains(settings.PausedUntilOfflineChannels, channel => channel == "alpha");

        Assert.Equal(
            StartDownloadResult.Offline,
            await service.TryStartDownloadAsync("alpha", CancellationToken.None));
        Assert.DoesNotContain(settings.PausedUntilOfflineChannels, channel => channel == "alpha");
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public async Task RepeatedChecksDoNotBypassSuppressionWhileChannelIsLive()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settings = CreateSettings(temporaryDirectory.Path);
        settings.PausedUntilOfflineChannels.Add("alpha");
        var playback = new ControlledPlaybackClient();
        playback.EnqueueLive("alpha");
        playback.EnqueueLive("alpha");
        var saveCount = 0;
        var service = CreateService(settings, playback, saveSettings: () => saveCount++);

        Assert.Equal(StartDownloadResult.Suppressed, await service.TryStartDownloadAsync("alpha", CancellationToken.None));
        Assert.Equal(StartDownloadResult.Suppressed, await service.TryStartDownloadAsync("alpha", CancellationToken.None));

        Assert.Empty(service.GetActiveDownloads());
        Assert.Contains(settings.PausedUntilOfflineChannels, channel => channel == "alpha");
        Assert.Equal(0, saveCount);
    }

    [Fact]
    public async Task StaleTelegramCallbackCannotStopReplacementSession()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settings = CreateSettings(temporaryDirectory.Path);
        var playback = new ControlledPlaybackClient();
        playback.EnqueueLive("alpha");
        playback.EnqueueOffline("alpha");
        playback.EnqueueLive("alpha");
        var service = CreateService(settings, playback);

        await service.TryStartDownloadAsync("alpha", CancellationToken.None);
        var oldSession = Assert.Single(service.GetActiveDownloads());
        Assert.True(Keyboards.TryParseStopDownloadCallback(
            Keyboards.CreateStopDownloadCallback(oldSession.SessionId),
            out var callbackSessionId));
        Assert.Equal(StopDownloadResult.Accepted, await service.RequestStopAsync(callbackSessionId, CancellationToken.None));
        await WaitUntilAsync(() => service.GetActiveDownloads().Count == 0);

        Assert.Equal(StartDownloadResult.Offline, await service.TryStartDownloadAsync("alpha", CancellationToken.None));
        Assert.Equal(StartDownloadResult.Started, await service.TryStartDownloadAsync("alpha", CancellationToken.None));
        var replacement = Assert.Single(service.GetActiveDownloads());

        Assert.NotEqual(oldSession.SessionId, replacement.SessionId);
        Assert.Equal(StopDownloadResult.NotFound, await service.RequestStopAsync(callbackSessionId, CancellationToken.None));
        Assert.Equal(replacement.SessionId, Assert.Single(service.GetActiveDownloads()).SessionId);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StopForShutdownAsync(timeout.Token);
    }

    [Fact]
    public void RemovingTrackedChannelAlsoRemovesItsSuppression()
    {
        var settings = new AppSettings
        {
            TrackedChannels = ["alpha"],
            PausedUntilOfflineChannels = ["ALPHA"]
        };

        var removed = settings.RemoveTrackedChannel("alpha");

        Assert.True(removed);
        Assert.Empty(settings.TrackedChannels);
        Assert.Empty(settings.PausedUntilOfflineChannels);
    }

    private static TwitchDownloaderService CreateService(
        AppSettings settings,
        ControlledPlaybackClient playback,
        ControlledMediaToolRunner? mediaTools = null,
        RecordingNotifications? notifications = null,
        Action? saveSettings = null)
    {
        return new TwitchDownloaderService(
            settings,
            playback,
            mediaTools ?? new ControlledMediaToolRunner(),
            notifications ?? new RecordingNotifications(),
            saveSettings ?? (() => { }),
            new ImmediateDelay());
    }

    private static AppSettings CreateSettings(string downloadPath)
    {
        return new AppSettings { DownloadPath = downloadPath };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class ControlledPlaybackClient : ITwitchPlaybackClient
    {
        private readonly ConcurrentDictionary<string, Queue<object>> _resolutions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Queue<MediaPlaylistResponse>> _playlists = new(StringComparer.OrdinalIgnoreCase);

        public ConcurrentDictionary<Uri, byte[]> Segments { get; } = new();
        public TaskCompletionSource SegmentFetched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void EnqueueLive(string channel)
        {
            EnqueueResolution(
                channel,
                new TwitchPlaybackResult(
                    TwitchPlaybackStatus.Live,
                    channel,
                    PlaylistUri(channel),
                    "source",
                    true));
        }

        public void EnqueueOffline(string channel)
        {
            EnqueueResolution(
                channel,
                new TwitchPlaybackResult(TwitchPlaybackStatus.Offline, channel, null, string.Empty, false));
        }

        public void EnqueueResolution(string channel, object resolution)
        {
            var queue = _resolutions.GetOrAdd(channel, _ => new Queue<object>());
            lock (queue)
                queue.Enqueue(resolution);
        }

        public void EnqueuePlaylist(string channel, string playlist)
        {
            var queue = _playlists.GetOrAdd(channel, _ => new Queue<MediaPlaylistResponse>());
            lock (queue)
                queue.Enqueue(new MediaPlaylistResponse(playlist, PlaylistUri(channel)));
        }

        public Task<TwitchPlaybackResult> ResolveLiveAsync(string channel, CancellationToken cancellationToken)
        {
            var queue = _resolutions[channel];
            object resolution;
            lock (queue)
                resolution = queue.Dequeue();
            return resolution is Exception exception
                ? Task.FromException<TwitchPlaybackResult>(exception)
                : Task.FromResult((TwitchPlaybackResult)resolution);
        }

        public async Task<MediaPlaylistResponse> FetchMediaPlaylistAsync(
            Uri playlistUri,
            CancellationToken cancellationToken)
        {
            var channel = playlistUri.Segments[^2].Trim('/');
            if (_playlists.TryGetValue(channel, out var queue))
            {
                lock (queue)
                {
                    if (queue.Count > 0)
                        return queue.Dequeue();
                }
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Infinite delay completed unexpectedly.");
        }

        public Task<byte[]?> FetchSegmentAsync(Uri segmentUri, CancellationToken cancellationToken)
        {
            SegmentFetched.TrySetResult();
            return Task.FromResult(Segments.TryGetValue(segmentUri, out var bytes) ? bytes : null);
        }

        private static Uri PlaylistUri(string channel) => new($"https://video.example/{channel}/index.m3u8");
    }

    private sealed class ControlledMediaToolRunner : IMediaToolRunner
    {
        public bool BlockFinalization { get; init; }
        public double DurationSeconds { get; init; } = 1;
        public TaskCompletionSource FinalizationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFinalization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MediaToolResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            if (executable == "ffprobe")
            {
                var duration = DurationSeconds.ToString("F3", CultureInfo.InvariantCulture);
                var output = $"codec_type=video\nduration={duration}\ncodec_type=audio\nduration={duration}\n";
                return new MediaToolResult(0, output, string.Empty);
            }

            if (arguments[^1] == "-")
                return new MediaToolResult(0, string.Empty, $"time=00:00:{DurationSeconds:00.00}");

            FinalizationStarted.TrySetResult();
            if (BlockFinalization)
                await ReleaseFinalization.Task.WaitAsync(cancellationToken);
            await File.WriteAllTextAsync(arguments[^1], "mp4", cancellationToken);
            return new MediaToolResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class RecordingNotifications : IRecordingNotificationSink
    {
        public TaskCompletionSource<RecordingCompletionInfo> Completed { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RecordingStartedAsync(RecordingStartedInfo info, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RecordingCompletedAsync(RecordingCompletionInfo info, CancellationToken cancellationToken)
        {
            Completed.TrySetResult(info);
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateDelay : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
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
