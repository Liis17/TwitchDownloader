using System.Globalization;
using System.Text.RegularExpressions;

namespace TwitchDownloader2.CLI
{
    internal sealed record HlsVariant(string Name, string Group, Uri Uri);

    internal sealed record HlsMasterPlaylist(IReadOnlyList<HlsVariant> Variants);

    internal sealed record HlsSegment(
        long Sequence,
        Uri Uri,
        double DurationSeconds,
        string Title,
        Uri? MapUri,
        bool IsAdvertisement);

    internal sealed record HlsAdAnnouncement(string Id, double DurationSeconds);

    internal sealed record HlsMediaPlaylist(
        TimeSpan TargetDuration,
        bool HasEndList,
        IReadOnlyList<HlsSegment> Segments,
        IReadOnlyList<HlsAdAnnouncement> AdAnnouncements);

    internal static partial class HlsPlaylistParser
    {
        public static HlsMasterPlaylist ParseMaster(string text, Uri playlistUri)
        {
            var namesByGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var variants = new List<HlsVariant>();
            string pendingGroup = string.Empty;

            foreach (var raw in SplitLines(text))
            {
                var line = raw.Trim();
                if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
                {
                    var attributes = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                    if (attributes.TryGetValue("GROUP-ID", out var group))
                    {
                        namesByGroup[group] = attributes.TryGetValue("NAME", out var mediaName) ? mediaName : group;
                    }
                    continue;
                }

                if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
                {
                    var attributes = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                    pendingGroup = attributes.TryGetValue("VIDEO", out var videoGroup)
                        ? videoGroup
                        : string.Empty;
                    continue;
                }

                if (pendingGroup.Length == 0 || line.Length == 0 || line[0] == '#')
                    continue;

                var name = namesByGroup.TryGetValue(pendingGroup, out var mappedName)
                    ? mappedName
                    : pendingGroup;
                variants.Add(new HlsVariant(name, pendingGroup, ResolveUri(playlistUri, line)));
                pendingGroup = string.Empty;
            }

            return new HlsMasterPlaylist(variants);
        }

        public static HlsMediaPlaylist ParseMedia(string text, Uri playlistUri)
        {
            var segments = new List<HlsSegment>();
            var announcements = new List<HlsAdAnnouncement>();
            long sequence = 0;
            double duration = 0;
            string title = string.Empty;
            Uri? mapUri = null;
            var targetDuration = TimeSpan.FromSeconds(2);
            bool hasEndList = false;

            foreach (var raw in SplitLines(text))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
                {
                    long.TryParse(line[(line.IndexOf(':') + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);
                    continue;
                }

                if (line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.Ordinal))
                {
                    if (double.TryParse(line[(line.IndexOf(':') + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                        targetDuration = TimeSpan.FromSeconds(seconds);
                    continue;
                }

                if (line.StartsWith("#EXT-X-ENDLIST", StringComparison.Ordinal))
                {
                    hasEndList = true;
                    continue;
                }

                if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
                {
                    var attributes = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                    if (attributes.TryGetValue("URI", out var value))
                        mapUri = ResolveUri(playlistUri, value);
                    continue;
                }

                if (line.StartsWith("#EXT-X-DATERANGE:", StringComparison.Ordinal)
                    && line.Contains("stitched-ad", StringComparison.OrdinalIgnoreCase))
                {
                    var attributes = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                    var id = attributes.TryGetValue("ID", out var value) ? value : line;
                    var announcedDuration = attributes.TryGetValue("DURATION", out var durationText)
                        && double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDuration)
                            ? parsedDuration
                            : 0;
                    announcements.Add(new HlsAdAnnouncement(id, announcedDuration));
                    continue;
                }

                if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
                {
                    var body = line[8..];
                    var commaIndex = body.IndexOf(',');
                    var durationText = commaIndex >= 0 ? body[..commaIndex] : body;
                    double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                    title = commaIndex >= 0 ? body[(commaIndex + 1)..].Trim() : string.Empty;
                    continue;
                }

                if (line[0] == '#')
                    continue;

                var isAdvertisement = title.Contains("amazon", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("stitched", StringComparison.OrdinalIgnoreCase);
                segments.Add(new HlsSegment(
                    sequence++,
                    ResolveUri(playlistUri, line),
                    duration,
                    title,
                    mapUri,
                    isAdvertisement));
                duration = 0;
                title = string.Empty;
            }

            return new HlsMediaPlaylist(targetDuration, hasEndList, segments, announcements);
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }

        private static Dictionary<string, string> ParseAttributes(string text)
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in AttributeRegex().Matches(text))
            {
                var value = match.Groups[2].Value;
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];
                attributes[match.Groups[1].Value] = value;
            }
            return attributes;
        }

        private static Uri ResolveUri(Uri playlistUri, string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(playlistUri, value);
        }

        [GeneratedRegex("([A-Z0-9-]+)=(\\\"[^\\\"]*\\\"|[^,]*)", RegexOptions.IgnoreCase)]
        private static partial Regex AttributeRegex();
    }
}
