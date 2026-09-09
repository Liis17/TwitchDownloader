using System.Net;
using System.Text;
using System.Text.Json;

namespace TwitchDownloader2.CLI
{
    public enum TwitchPlaybackStatus
    {
        Live,
        Offline
    }

    public sealed record TwitchPlaybackResult(
        TwitchPlaybackStatus Status,
        string Channel,
        Uri? MediaPlaylistUrl,
        string SelectedQuality,
        bool IsSource);

    public sealed record MediaPlaylistResponse(string Content, Uri PlaylistUri);

    public interface ITwitchPlaybackClient
    {
        Task<TwitchPlaybackResult> ResolveLiveAsync(string channel, CancellationToken cancellationToken);
        Task<MediaPlaylistResponse> FetchMediaPlaylistAsync(Uri playlistUri, CancellationToken cancellationToken);
        Task<byte[]?> FetchSegmentAsync(Uri segmentUri, CancellationToken cancellationToken);
    }

    public sealed class TwitchPlaybackClient : ITwitchPlaybackClient
    {
        private const string ClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
        private const string PlaybackQuery = "query PlaybackAccessToken_Template($login: String!, $isLive: Boolean!, $vodID: ID!, $isVod: Boolean!, $playerType: String!) { streamPlaybackAccessToken(channelName: $login, params: {platform: \"web\", playerBackend: \"mediaplayer\", playerType: $playerType}) @include(if: $isLive) { value signature } videoPlaybackAccessToken(id: $vodID, params: {platform: \"web\", playerBackend: \"mediaplayer\", playerType: $playerType}) @include(if: $isVod) { value signature } }";
        private static readonly TimeSpan PlaylistTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan SegmentTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _httpClient;

        public TwitchPlaybackClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<TwitchPlaybackResult> ResolveLiveAsync(string channel, CancellationToken cancellationToken)
        {
            channel = NormalizeChannel(channel);
            var token = await GetPlaybackTokenAsync(channel, cancellationToken);
            if (token is null)
                return Offline(channel);

            var masterUri = BuildMasterPlaylistUri(channel, token.Value.Value, token.Value.Signature);
            using var request = new HttpRequestMessage(HttpMethod.Get, masterUri);
            using var response = await SendAsync(request, PlaylistTimeout, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Offline(channel);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Twitch Usher returned HTTP {(int)response.StatusCode}: {TrimForLog(body)}");

            var finalMasterUri = response.RequestMessage?.RequestUri ?? masterUri;
            var master = HlsPlaylistParser.ParseMaster(body, finalMasterUri);
            if (master.Variants.Count == 0)
                throw new InvalidDataException("Twitch master playlist contains no stream variants.");

            var selected = master.Variants.FirstOrDefault(IsSourceVariant) ?? master.Variants[0];
            var isSource = IsSourceVariant(selected);
            return new TwitchPlaybackResult(
                TwitchPlaybackStatus.Live,
                channel,
                selected.Uri,
                string.IsNullOrWhiteSpace(selected.Name) ? selected.Group : selected.Name,
                isSource);
        }

        public async Task<MediaPlaylistResponse> FetchMediaPlaylistAsync(Uri playlistUri, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, playlistUri);
            using var response = await SendAsync(request, PlaylistTimeout, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Twitch media playlist returned HTTP {(int)response.StatusCode}: {TrimForLog(body)}");

            return new MediaPlaylistResponse(body, response.RequestMessage?.RequestUri ?? playlistUri);
        }

        public async Task<byte[]?> FetchSegmentAsync(Uri segmentUri, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, segmentUri);
                    using var response = await SendAsync(request, SegmentTimeout, cancellationToken);
                    if (response.IsSuccessStatusCode)
                        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Per-request timeout: retry below.
                }
                catch (HttpRequestException)
                {
                    // Transient CDN failure: retry below.
                }

                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), cancellationToken);
            }

            return null;
        }

        private async Task<(string Value, string Signature)?> GetPlaybackTokenAsync(
            string channel,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                operationName = "PlaybackAccessToken_Template",
                variables = new
                {
                    login = channel,
                    isLive = true,
                    isVod = false,
                    vodID = string.Empty,
                    playerType = "site"
                },
                query = PlaybackQuery
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Client-ID", ClientId);

            using var response = await SendAsync(request, PlaylistTimeout, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Twitch GQL returned HTTP {(int)response.StatusCode}: {TrimForLog(body)}");

            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("errors", out var errors))
                throw new InvalidDataException($"Twitch GQL error: {TrimForLog(errors.GetRawText())}");

            if (!json.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("streamPlaybackAccessToken", out var tokenElement)
                || tokenElement.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var value = tokenElement.GetProperty("value").GetString();
            var signature = tokenElement.GetProperty("signature").GetString();
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(signature))
                return null;

            try
            {
                using var tokenJson = JsonDocument.Parse(value);
                if (tokenJson.RootElement.TryGetProperty("authorization", out var authorization)
                    && authorization.TryGetProperty("forbidden", out var forbidden)
                    && forbidden.ValueKind == JsonValueKind.True)
                {
                    var reason = authorization.TryGetProperty("reason", out var reasonElement)
                        ? reasonElement.GetString()
                        : "unknown reason";
                    throw new UnauthorizedAccessException($"Twitch playback is forbidden: {reason}");
                }
            }
            catch (JsonException)
            {
                // Twitch normally returns JSON in value; opaque values are still valid tokens.
            }

            return (value, signature);
        }

        private async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        }

        private static Uri BuildMasterPlaylistUri(string channel, string token, string signature)
        {
            var parameters = new Dictionary<string, string>
            {
                ["token"] = token,
                ["sig"] = signature,
                ["nauth"] = token,
                ["nauthsig"] = signature,
                ["allow_source"] = "true",
                ["allow_audio_only"] = "true",
                ["fast_bread"] = "true",
                ["player"] = "twitchweb",
                ["player_backend"] = "mediaplayer",
                ["playlist_include_framerate"] = "true",
                ["reassignments_supported"] = "true",
                ["supported_codecs"] = "avc1",
                ["transcode_mode"] = "cbr_v1",
                ["p"] = Random.Shared.Next(1_000_000).ToString()
            };
            var query = string.Join('&', parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
            return new Uri($"https://usher.ttvnw.net/api/channel/hls/{channel}.m3u8?{query}");
        }

        private static bool IsSourceVariant(HlsVariant variant)
        {
            return string.Equals(variant.Group, "chunked", StringComparison.OrdinalIgnoreCase)
                || variant.Name.Contains("(source)", StringComparison.OrdinalIgnoreCase)
                || variant.Uri.AbsolutePath.Contains("/chunked/", StringComparison.OrdinalIgnoreCase);
        }

        private static TwitchPlaybackResult Offline(string channel)
        {
            return new TwitchPlaybackResult(TwitchPlaybackStatus.Offline, channel, null, string.Empty, false);
        }

        private static string NormalizeChannel(string channel)
        {
            var normalized = channel?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized.Length is < 3 or > 25
                || normalized.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            {
                throw new ArgumentException("Twitch channel must contain 3-25 letters, digits or underscores.", nameof(channel));
            }
            return normalized;
        }

        private static string TrimForLog(string text)
        {
            var compact = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return compact.Length <= 200 ? compact : compact[..200];
        }
    }
}
