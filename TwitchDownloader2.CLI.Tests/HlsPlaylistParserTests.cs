using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class HlsPlaylistParserTests
{
    [Fact]
    public void ParseMediaPlaylist_ResolvesSegmentsAdsAndFmp4Map()
    {
        var playlistUri = new Uri("https://video.example/live/index.m3u8");
        const string playlist = """
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXT-X-MEDIA-SEQUENCE:42
            #EXT-X-DATERANGE:ID="ad-1",CLASS="twitch-stitched-ad",DURATION=30.0
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:2.0,live
            segment42.m4s
            #EXT-X-TWITCH-PREFETCH:prefetch.m4s
            #EXTINF:2.0,Amazon
            segment43.m4s
            #EXT-X-ENDLIST
            """;

        var result = HlsPlaylistParser.ParseMedia(playlist, playlistUri);

        Assert.Equal(TimeSpan.FromSeconds(2), result.TargetDuration);
        Assert.True(result.HasEndList);
        Assert.Collection(
            result.Segments,
            segment =>
            {
                Assert.Equal(42, segment.Sequence);
                Assert.Equal(new Uri("https://video.example/live/segment42.m4s"), segment.Uri);
                Assert.Equal(new Uri("https://video.example/live/init.mp4"), segment.MapUri);
                Assert.False(segment.IsAdvertisement);
            },
            segment =>
            {
                Assert.Equal(43, segment.Sequence);
                Assert.True(segment.IsAdvertisement);
            });
        Assert.Collection(result.AdAnnouncements, ad =>
        {
            Assert.Equal("ad-1", ad.Id);
            Assert.Equal(30, ad.DurationSeconds);
        });
    }

    [Fact]
    public void ParseMediaPlaylist_DoesNotTreatDateRangeAsSegmentAdvertisement()
    {
        var playlistUri = new Uri("https://video.example/live/index.m3u8");
        const string playlist = """
            #EXTM3U
            #EXT-X-MEDIA-SEQUENCE:7
            #EXT-X-DATERANGE:ID="ad-2",CLASS="twitch-stitched-ad",DURATION=15
            #EXTINF:2.0,live
            content.ts
            """;

        var result = HlsPlaylistParser.ParseMedia(playlist, playlistUri);

        var segment = Assert.Single(result.Segments);
        Assert.False(segment.IsAdvertisement);
        Assert.Single(result.AdAnnouncements);
    }
}
