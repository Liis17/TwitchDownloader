namespace TwitchDownloader2.CLI
{
    internal enum RecordingEndReason
    {
        EndList,
        Offline,
        Cancelled,
        Failed
    }

    internal enum RecordingContainer
    {
        MpegTs,
        FragmentedMp4
    }

    internal sealed record LiveRecordingResult(
        string RawPath,
        RecordingContainer Container,
        RecordingEndReason EndReason,
        double ContentDurationSeconds,
        long BytesWritten,
        int SegmentCount,
        int AdvertisementCount,
        double AdvertisementDurationSeconds,
        int GapCount,
        string? ErrorMessage)
    {
        public bool HasContent => SegmentCount > 0;
    }

    internal sealed class LiveStreamRecorder
    {
        private readonly ITwitchPlaybackClient _playbackClient;
        private readonly IAsyncDelay _delay;
        private readonly Action<string>? _log;

        public LiveStreamRecorder(
            ITwitchPlaybackClient playbackClient,
            IAsyncDelay? delay = null,
            Action<string>? log = null)
        {
            _playbackClient = playbackClient;
            _delay = delay ?? new SystemAsyncDelay();
            _log = log;
        }

        /// <summary>
        /// Загрузка VOD: плейлист читается один раз, сегменты скачиваются параллельно
        /// (не больше <paramref name="parallelism"/> одновременно) и пишутся в сырьё строго по порядку.
        /// </summary>
        public async Task<LiveRecordingResult> DownloadVodAsync(
            string label,
            Uri playlistUri,
            string rawPath,
            int parallelism,
            Action<int, int>? progress,
            CancellationToken cancellationToken)
        {
            var segmentCount = 0;
            var contentDuration = 0d;
            var gapCount = 0;
            long bytesWritten = 0;
            Uri? lastWrittenMap = null;
            var container = RecordingContainer.MpegTs;
            var endReason = RecordingEndReason.Failed;
            string? errorMessage = null;
            using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var pending = new Queue<(HlsSegment Segment, Task<byte[]?> Download)>();

            Directory.CreateDirectory(Path.GetDirectoryName(rawPath) ?? ".");

            try
            {
                await using var output = new FileStream(
                    rawPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var playlist = await FetchVodPlaylistAsync(playlistUri, cancellationToken);
                var segments = playlist.Segments.Where(segment => !segment.IsAdvertisement).ToArray();
                var processed = 0;
                progress?.Invoke(0, segments.Length);

                async Task WriteNextAsync()
                {
                    var (segment, download) = pending.Dequeue();
                    var bytes = await download;
                    processed++;
                    if (bytes is null)
                    {
                        gapCount++;
                        _log?.Invoke($"VOD segment {segment.Sequence} was not downloaded for '{label}'.");
                        progress?.Invoke(processed, segments.Length);
                        return;
                    }

                    if (segment.MapUri is not null && segment.MapUri != lastWrittenMap)
                    {
                        var initializationBytes = await _playbackClient.FetchSegmentAsync(segment.MapUri, cancellationToken)
                            ?? throw new InvalidDataException("HLS initialization segment was not downloaded.");
                        await output.WriteAsync(initializationBytes, cancellationToken);
                        bytesWritten += initializationBytes.Length;
                        lastWrittenMap = segment.MapUri;
                        container = RecordingContainer.FragmentedMp4;
                    }

                    await output.WriteAsync(bytes, cancellationToken);
                    bytesWritten += bytes.Length;
                    segmentCount++;
                    contentDuration += segment.DurationSeconds;
                    progress?.Invoke(processed, segments.Length);
                }

                foreach (var segment in segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (pending.Count >= Math.Max(1, parallelism))
                        await WriteNextAsync();
                    pending.Enqueue((segment, FetchVodSegmentAsync(segment.Uri, downloadCancellation.Token)));
                }

                while (pending.Count > 0)
                    await WriteNextAsync();

                endReason = RecordingEndReason.EndList;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                endReason = RecordingEndReason.Cancelled;
            }
            catch (Exception ex)
            {
                endReason = RecordingEndReason.Failed;
                errorMessage = ex.Message;
            }
            finally
            {
                // Незавершённые параллельные скачивания отменяются и дожидаются, чтобы не оставить фоновые запросы.
                downloadCancellation.Cancel();
                foreach (var (_, download) in pending)
                {
                    try { await download; } catch { }
                }
            }

            if (segmentCount == 0)
            {
                try { File.Delete(rawPath); } catch { }
            }

            return new LiveRecordingResult(
                rawPath,
                container,
                endReason,
                contentDuration,
                bytesWritten,
                segmentCount,
                0,
                0,
                gapCount,
                errorMessage);
        }

        private async Task<HlsMediaPlaylist> FetchVodPlaylistAsync(Uri playlistUri, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var response = await _playbackClient.FetchMediaPlaylistAsync(playlistUri, cancellationToken);
                    return HlsPlaylistParser.ParseMedia(response.Content, response.PlaylistUri);
                }
                catch (Exception) when (attempt < 3 && !cancellationToken.IsCancellationRequested)
                {
                    await _delay.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }

        // У VOD с заглушённым звуком Twitch иногда отдаёт 403 на "-unmuted"-сегменты; тогда берётся "-muted"-вариант.
        private async Task<byte[]?> FetchVodSegmentAsync(Uri segmentUri, CancellationToken cancellationToken)
        {
            var bytes = await _playbackClient.FetchSegmentAsync(segmentUri, cancellationToken);
            if (bytes is not null)
                return bytes;

            var text = segmentUri.ToString();
            return text.Contains("-unmuted.", StringComparison.Ordinal)
                ? await _playbackClient.FetchSegmentAsync(new Uri(text.Replace("-unmuted.", "-muted.", StringComparison.Ordinal)), cancellationToken)
                : null;
        }

        public async Task<LiveRecordingResult> RecordAsync(
            string channel,
            Uri initialPlaylistUri,
            string rawPath,
            CancellationToken cancellationToken)
        {
            long? nextSequence = null;
            var mediaPlaylistUri = initialPlaylistUri;
            var targetDuration = TimeSpan.FromSeconds(2);
            var playlistFailureCount = 0;
            var segmentCount = 0;
            var advertisementCount = 0;
            var advertisementDuration = 0d;
            var contentDuration = 0d;
            var gapCount = 0;
            long bytesWritten = 0;
            Uri? lastWrittenMap = null;
            var announcedAdvertisements = new HashSet<string>(StringComparer.Ordinal);
            var container = RecordingContainer.MpegTs;
            var endReason = RecordingEndReason.Failed;
            string? errorMessage = null;

            Directory.CreateDirectory(Path.GetDirectoryName(rawPath) ?? ".");

            try
            {
                await using var output = new FileStream(
                    rawPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MediaPlaylistResponse response;
                    try
                    {
                        response = await _playbackClient.FetchMediaPlaylistAsync(mediaPlaylistUri, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        playlistFailureCount++;
                        if (playlistFailureCount < 3)
                        {
                            await _delay.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                            continue;
                        }

                        try
                        {
                            var refreshed = await _playbackClient.ResolveLiveAsync(channel, cancellationToken);
                            if (refreshed.Status == TwitchPlaybackStatus.Offline || refreshed.MediaPlaylistUrl is null)
                            {
                                endReason = RecordingEndReason.Offline;
                                break;
                            }

                            mediaPlaylistUri = refreshed.MediaPlaylistUrl;
                            playlistFailureCount = 0;
                            _log?.Invoke($"Twitch playlist was refreshed for '{channel}'.");
                            continue;
                        }
                        catch (Exception refreshException)
                        {
                            endReason = RecordingEndReason.Failed;
                            errorMessage = $"Playlist refresh failed after '{ex.Message}': {refreshException.Message}";
                            break;
                        }
                    }

                    playlistFailureCount = 0;
                    mediaPlaylistUri = response.PlaylistUri;
                    var playlist = HlsPlaylistParser.ParseMedia(response.Content, response.PlaylistUri);
                    targetDuration = playlist.TargetDuration;

                    foreach (var announcement in playlist.AdAnnouncements)
                    {
                        if (announcedAdvertisements.Add(announcement.Id))
                        {
                            _log?.Invoke(
                                $"Twitch announced stitched ad '{announcement.Id}' ({announcement.DurationSeconds:F1}s) for '{channel}'; segment titles remain authoritative.");
                        }
                    }

                    foreach (var segment in playlist.Segments)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (nextSequence.HasValue && segment.Sequence < nextSequence.Value)
                            continue;

                        if (nextSequence.HasValue && segment.Sequence > nextSequence.Value)
                        {
                            var gap = checked((int)(segment.Sequence - nextSequence.Value));
                            gapCount += gap;
                            _log?.Invoke($"CDN skipped {gap} HLS segment(s) for '{channel}'.");
                        }

                        nextSequence = segment.Sequence + 1;
                        if (segment.IsAdvertisement)
                        {
                            advertisementCount++;
                            advertisementDuration += segment.DurationSeconds;
                            continue;
                        }

                        if (segment.MapUri is not null && segment.MapUri != lastWrittenMap)
                        {
                            var initializationBytes = await _playbackClient.FetchSegmentAsync(segment.MapUri, cancellationToken);
                            if (initializationBytes is null)
                            {
                                gapCount++;
                                _log?.Invoke($"HLS initialization segment was not downloaded for '{channel}'.");
                                continue;
                            }

                            await output.WriteAsync(initializationBytes, cancellationToken);
                            bytesWritten += initializationBytes.Length;
                            lastWrittenMap = segment.MapUri;
                            container = RecordingContainer.FragmentedMp4;
                        }

                        var bytes = await _playbackClient.FetchSegmentAsync(segment.Uri, cancellationToken);
                        if (bytes is null)
                        {
                            gapCount++;
                            _log?.Invoke($"HLS segment {segment.Sequence} was not downloaded for '{channel}'.");
                            continue;
                        }

                        await output.WriteAsync(bytes, cancellationToken);
                        bytesWritten += bytes.Length;
                        segmentCount++;
                        contentDuration += segment.DurationSeconds;
                    }

                    if (playlist.HasEndList)
                    {
                        endReason = RecordingEndReason.EndList;
                        break;
                    }

                    var delay = TimeSpan.FromMilliseconds(Math.Max(700, targetDuration.TotalMilliseconds / 2));
                    await _delay.DelayAsync(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                endReason = RecordingEndReason.Cancelled;
            }
            catch (Exception ex)
            {
                endReason = RecordingEndReason.Failed;
                errorMessage = ex.Message;
            }

            if (segmentCount == 0)
            {
                try { File.Delete(rawPath); } catch { }
            }

            return new LiveRecordingResult(
                rawPath,
                container,
                endReason,
                contentDuration,
                bytesWritten,
                segmentCount,
                advertisementCount,
                advertisementDuration,
                gapCount,
                errorMessage);
        }
    }
}
