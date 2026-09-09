# v2: TwitchPlaybackClient и HLS parser

Parent: [[Index]]

## Назначение

Получает playback-токен и master playlist напрямую через Twitch GraphQL/Usher,
выбирает source-вариант и разбирает media playlist без участия `yt-dlp`.

## Файлы

- `TwitchDownloader2.CLI/TwitchPlaybackClient.cs` — HTTP-клиент Twitch и повторная загрузка сегментов.
- `TwitchDownloader2.CLI/HlsPlaylistParser.cs` — разбор master/media HLS playlist.

## Ключевые методы

| Метод | Описание |
|-------|----------|
| `ResolveLiveAsync(channel: string, cancellationToken: CancellationToken): Task<TwitchPlaybackResult>` | Получает live playback-токен, выбирает source или первый доступный вариант и различает live/offline. |
| `FetchMediaPlaylistAsync(playlistUri: Uri, cancellationToken: CancellationToken): Task<MediaPlaylistResponse>` | Загружает media playlist с 15-секундным таймаутом. |
| `FetchSegmentAsync(segmentUri: Uri, cancellationToken: CancellationToken): Task<byte[]?>` | Загружает сегмент с 30-секундным таймаутом и четырьмя попытками. |
| `ParseMaster(text: string, playlistUri: Uri): HlsMasterPlaylist` | Разбирает варианты качества и разрешает относительные URI. |
| `ParseMedia(text: string, playlistUri: Uri): HlsMediaPlaylist` | Извлекает sequence, EXTINF, ENDLIST, DATERANGE и EXT-X-MAP. |

## Важные детали

- Source определяется по группе `chunked`, пометке `(source)` или `/chunked/` в URI.
- Реклама привязывается к сегменту только по заголовку `EXTINF` (`amazon`/`stitched`).
  `DATERANGE stitched-ad` сохраняется отдельно только как диагностический сигнал.
- `EXT-X-TWITCH-PREFETCH` не превращается в готовый сегмент.
- Используется публичный Client-ID web-плеера Twitch; детали неофициального GraphQL/Usher
  взаимодействия локализованы в одном модуле.

## Зависимости

- Использует: Twitch GraphQL, Twitch Usher/CDN, `HttpClient`.
- Будет использоваться в: [[modules/v2-twitch-downloader]], [[modules/v2-twitch-checker]].
