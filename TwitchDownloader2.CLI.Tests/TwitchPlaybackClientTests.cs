using System.Net;
using System.Text;
using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class TwitchPlaybackClientTests
{
    [Fact]
    public async Task ResolveLiveAsync_SelectsSourceVariant()
    {
        var handler = new QueuedHttpMessageHandler(
            JsonResponse("""{"data":{"streamPlaybackAccessToken":{"value":"token","signature":"sig"}}}"""),
            TextResponse("""
                #EXTM3U
                #EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID="720p60",NAME="720p60"
                #EXT-X-STREAM-INF:BANDWIDTH=3000000,VIDEO="720p60"
                https://video.example/720p60/index-dvr.m3u8
                #EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID="chunked",NAME="1080p60 (source)"
                #EXT-X-STREAM-INF:BANDWIDTH=6000000,VIDEO="chunked"
                https://video.example/chunked/index-dvr.m3u8
                """));

        using var httpClient = new HttpClient(handler);
        var client = new TwitchPlaybackClient(httpClient);

        var result = await client.ResolveLiveAsync("some_channel", CancellationToken.None);

        Assert.Equal(TwitchPlaybackStatus.Live, result.Status);
        Assert.Equal("some_channel", result.Channel);
        Assert.Equal("https://video.example/chunked/index-dvr.m3u8", result.MediaPlaylistUrl?.AbsoluteUri);
        Assert.Equal("1080p60 (source)", result.SelectedQuality);
        Assert.True(result.IsSource);
    }

    [Fact]
    public async Task ResolveLiveAsync_FallsBackToFirstVariantWhenSourceIsMissing()
    {
        var handler = new QueuedHttpMessageHandler(
            JsonResponse("""{"data":{"streamPlaybackAccessToken":{"value":"token","signature":"sig"}}}"""),
            TextResponse("""
                #EXTM3U
                #EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID="720p60",NAME="720p60"
                #EXT-X-STREAM-INF:BANDWIDTH=3000000,VIDEO="720p60"
                https://video.example/720p60/index-dvr.m3u8
                #EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID="480p",NAME="480p"
                #EXT-X-STREAM-INF:BANDWIDTH=1200000,VIDEO="480p"
                https://video.example/480p/index-dvr.m3u8
                """));

        using var httpClient = new HttpClient(handler);
        var client = new TwitchPlaybackClient(httpClient);

        var result = await client.ResolveLiveAsync("some_channel", CancellationToken.None);

        Assert.Equal(TwitchPlaybackStatus.Live, result.Status);
        Assert.Equal("https://video.example/720p60/index-dvr.m3u8", result.MediaPlaylistUrl?.AbsoluteUri);
        Assert.Equal("720p60", result.SelectedQuality);
        Assert.False(result.IsSource);
    }

    [Fact]
    public async Task ResolveLiveAsync_ReturnsOfflineWhenUsherReturnsNotFound()
    {
        var handler = new QueuedHttpMessageHandler(
            JsonResponse("""{"data":{"streamPlaybackAccessToken":{"value":"token","signature":"sig"}}}"""),
            new HttpResponseMessage(HttpStatusCode.NotFound));

        using var httpClient = new HttpClient(handler);
        var client = new TwitchPlaybackClient(httpClient);

        var result = await client.ResolveLiveAsync("some_channel", CancellationToken.None);

        Assert.Equal(TwitchPlaybackStatus.Offline, result.Status);
        Assert.Null(result.MediaPlaylistUrl);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage TextResponse(string text)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "application/vnd.apple.mpegurl")
        };
    }

    private sealed class QueuedHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException($"Unexpected HTTP request: {request.Method} {request.RequestUri}");

            var response = _responses.Dequeue();
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
