using System.Text;
using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class LiveStreamRecorderTests
{
    [Fact]
    public async Task RecordAsync_WritesUniqueContentAndSkipsAdvertisements()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var initialUri = new Uri("https://video.example/live/index.m3u8");
        var playbackClient = new FakePlaybackClient(
            new MediaPlaylistResponse("""
                #EXTM3U
                #EXT-X-TARGETDURATION:2
                #EXT-X-MEDIA-SEQUENCE:10
                #EXTINF:2,live
                content10.ts
                #EXTINF:2,Amazon
                ad11.ts
                """, initialUri),
            new MediaPlaylistResponse("""
                #EXTM3U
                #EXT-X-TARGETDURATION:2
                #EXT-X-MEDIA-SEQUENCE:10
                #EXTINF:2,live
                content10.ts
                #EXTINF:2,Amazon
                ad11.ts
                #EXTINF:2,live
                content12.ts
                #EXT-X-ENDLIST
                """, initialUri));
        playbackClient.Segments[new Uri("https://video.example/live/content10.ts")] = Encoding.UTF8.GetBytes("first");
        playbackClient.Segments[new Uri("https://video.example/live/content12.ts")] = Encoding.UTF8.GetBytes("second");

        var rawPath = Path.Combine(temporaryDirectory.Path, "session.recording");
        var recorder = new LiveStreamRecorder(playbackClient, new ImmediateDelay());

        var result = await recorder.RecordAsync("channel", initialUri, rawPath, CancellationToken.None);

        Assert.Equal("firstsecond", await File.ReadAllTextAsync(rawPath));
        Assert.Equal(2, result.SegmentCount);
        Assert.Equal(1, result.AdvertisementCount);
        Assert.Equal(2, result.AdvertisementDurationSeconds);
        Assert.Equal(4, result.ContentDurationSeconds);
        Assert.Equal(0, result.GapCount);
        Assert.Equal(RecordingEndReason.EndList, result.EndReason);
    }

    [Fact]
    public async Task RecordAsync_WritesFmp4InitializationSegmentOnlyOnce()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var playlistUri = new Uri("https://video.example/live/index.m3u8");
        var playbackClient = new FakePlaybackClient(
            new MediaPlaylistResponse("""
                #EXTM3U
                #EXT-X-MEDIA-SEQUENCE:20
                #EXT-X-MAP:URI="init.mp4"
                #EXTINF:2,live
                content20.m4s
                #EXTINF:2,live
                content21.m4s
                #EXT-X-ENDLIST
                """, playlistUri));
        var mapUri = new Uri("https://video.example/live/init.mp4");
        playbackClient.Segments[mapUri] = Encoding.UTF8.GetBytes("init");
        playbackClient.Segments[new Uri("https://video.example/live/content20.m4s")] = Encoding.UTF8.GetBytes("first");
        playbackClient.Segments[new Uri("https://video.example/live/content21.m4s")] = Encoding.UTF8.GetBytes("second");

        var rawPath = Path.Combine(temporaryDirectory.Path, "session.recording");
        var recorder = new LiveStreamRecorder(playbackClient, new ImmediateDelay());

        var result = await recorder.RecordAsync("channel", playlistUri, rawPath, CancellationToken.None);

        Assert.Equal("initfirstsecond", await File.ReadAllTextAsync(rawPath));
        Assert.Equal(RecordingContainer.FragmentedMp4, result.Container);
        Assert.Equal(1, playbackClient.SegmentRequests.Count(uri => uri == mapUri));
    }

    [Fact]
    public async Task RecordAsync_RefreshesPlaybackAfterThreePlaylistFailures()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var initialUri = new Uri("https://video.example/old/index.m3u8");
        var refreshedUri = new Uri("https://video.example/new/index.m3u8");
        var playbackClient = new FakePlaybackClient(
            new HttpRequestException("first failure"),
            new HttpRequestException("second failure"),
            new HttpRequestException("third failure"),
            new MediaPlaylistResponse("""
                #EXTM3U
                #EXT-X-MEDIA-SEQUENCE:30
                #EXTINF:2,live
                content30.ts
                #EXT-X-ENDLIST
                """, refreshedUri))
        {
            RefreshResult = new TwitchPlaybackResult(
                TwitchPlaybackStatus.Live,
                "channel",
                refreshedUri,
                "1080p60 (source)",
                true)
        };
        playbackClient.Segments[new Uri("https://video.example/new/content30.ts")] = Encoding.UTF8.GetBytes("content");

        var rawPath = Path.Combine(temporaryDirectory.Path, "session.recording");
        var recorder = new LiveStreamRecorder(playbackClient, new ImmediateDelay());

        var result = await recorder.RecordAsync("channel", initialUri, rawPath, CancellationToken.None);

        Assert.Equal(1, playbackClient.ResolveCount);
        Assert.Equal("content", await File.ReadAllTextAsync(rawPath));
        Assert.Equal(RecordingEndReason.EndList, result.EndReason);
    }

    [Fact]
    public async Task RecordAsync_CountsMissingSequencesAndFailedSegmentsAsGaps()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var playlistUri = new Uri("https://video.example/live/index.m3u8");
        var playbackClient = new FakePlaybackClient(
            new MediaPlaylistResponse("""
                #EXTM3U
                #EXT-X-MEDIA-SEQUENCE:40
                #EXTINF:2,live
                content40.ts
                """, playlistUri),
            new MediaPlaylistResponse("""
                #EXTM3U
                #EXT-X-MEDIA-SEQUENCE:42
                #EXTINF:2,live
                content42.ts
                #EXTINF:2,live
                missing43.ts
                #EXT-X-ENDLIST
                """, playlistUri));
        playbackClient.Segments[new Uri("https://video.example/live/content40.ts")] = Encoding.UTF8.GetBytes("first");
        playbackClient.Segments[new Uri("https://video.example/live/content42.ts")] = Encoding.UTF8.GetBytes("second");

        var rawPath = Path.Combine(temporaryDirectory.Path, "session.recording");
        var recorder = new LiveStreamRecorder(playbackClient, new ImmediateDelay());

        var result = await recorder.RecordAsync("channel", playlistUri, rawPath, CancellationToken.None);

        Assert.Equal(2, result.GapCount);
        Assert.Equal("firstsecond", await File.ReadAllTextAsync(rawPath));
    }

    private sealed class FakePlaybackClient(params object[] playlists) : ITwitchPlaybackClient
    {
        private readonly Queue<object> _playlists = new(playlists);

        public Dictionary<Uri, byte[]> Segments { get; } = new();
        public List<Uri> SegmentRequests { get; } = new();
        public TwitchPlaybackResult? RefreshResult { get; init; }
        public int ResolveCount { get; private set; }

        public Task<TwitchPlaybackResult> ResolveLiveAsync(string channel, CancellationToken cancellationToken)
        {
            ResolveCount++;
            return Task.FromResult(RefreshResult ?? throw new InvalidOperationException("Playback refresh was not expected."));
        }

        public Task<MediaPlaylistResponse> FetchMediaPlaylistAsync(Uri playlistUri, CancellationToken cancellationToken)
        {
            var next = _playlists.Dequeue();
            return next is Exception exception
                ? Task.FromException<MediaPlaylistResponse>(exception)
                : Task.FromResult((MediaPlaylistResponse)next);
        }

        public Task<byte[]?> FetchSegmentAsync(Uri segmentUri, CancellationToken cancellationToken)
        {
            SegmentRequests.Add(segmentUri);
            return Task.FromResult(Segments.TryGetValue(segmentUri, out var bytes) ? bytes : null);
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
