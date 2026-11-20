# v2: TwitchDownloader2.CLI.Tests

Parent: [[Index]]

## Назначение

xUnit-проект для серверного контура v2. Проверяет сетевую и media-логику через
подставные `HttpMessageHandler`, process runner и delay, не запуская Twitch или Telegram.

## Файлы

- `TwitchDownloader2.CLI.Tests/TwitchPlaybackClientTests.cs` — source/fallback и HTTP-сценарии.
- `TwitchDownloader2.CLI.Tests/HlsPlaylistParserTests.cs` — sequence, реклама и `EXT-X-MAP`.
- `TwitchDownloader2.CLI.Tests/LiveStreamRecorderTests.cs` — dedupe, дыры, retry и token refresh;
  параллельная загрузка VOD с порядком записи и заменой `-unmuted` на `-muted`.
- `TwitchDownloader2.CLI.Tests/MediaFinalizerTests.cs` — MP4 validation, failed-artifacts и recovery.
- `TwitchDownloader2.CLI.Tests/TwitchDownloaderServiceTests.cs` — одна recording-сессия на канал,
  мгновенное уведомление естественного конца, параллельная финализация/replacement, отмена,
  pause, устаревшая кнопка остановки и shutdown/recovery; загрузка VOD без уведомления о конце
  эфира; ручная запись игнорирует паузу и не ставит её при остановке.
- `TwitchDownloader2.CLI.Tests/TwitchCheckerServiceTests.cs` — scheduler и `ForceCheck` без обхода pause.
- `TwitchDownloader2.CLI.Tests/KeyboardsTests.cs` — callback остановки содержит session ID, а не имя канала;
  codec callback удаления канала; добавление/удаление вынесены из главного меню в меню каналов;
  разбор ссылки `⬇️ Загрузить` на VOD и канал.
- `TwitchDownloader2.CLI.Tests/RecordingHistoryTests.cs` — SQLite-история переживает переоткрытие,
  сортируется от новых к старым, миграция базы без колонок `kind`/`vod_id`; декоратор сохраняет
  итог и передаёт уведомление дальше.
- `TwitchDownloader2.CLI.Tests/AppSettingsTests.cs` — сериализация state mutation и persistence.

## Запуск

```bash
dotnet test TwitchDownloader.sln
```

## Инварианты

- Тесты не требуют Twitch credentials, Telegram token, `ffmpeg` или Docker.
- Реальные `ffmpeg`/`ffprobe`, Twitch и Telegram автоматическими тестами не покрываются; их проверяют ручным запуском.
- Legacy `TwitchDownloader.CLI` не является тестовой целью этого проекта.

## Зависимости

- Тестирует: [[modules/v2-twitch-playback]], [[modules/v2-media-recording]],
  [[modules/v2-twitch-downloader]], [[modules/v2-twitch-checker]],
  [[modules/v2-keyboards]], [[modules/v2-app-settings]], [[modules/v2-recording-history]].
