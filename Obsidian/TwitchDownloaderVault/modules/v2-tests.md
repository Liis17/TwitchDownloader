# v2: TwitchDownloader2.CLI.Tests

Parent: [[Index]]

## Назначение

xUnit-проект для серверного контура v2. Проверяет сетевую и media-логику через
подставные `HttpMessageHandler`, process runner и delay, не запуская Twitch или Telegram.

## Файлы

- `TwitchDownloader2.CLI.Tests/TwitchPlaybackClientTests.cs` — source/fallback и HTTP-сценарии.
- `TwitchDownloader2.CLI.Tests/HlsPlaylistParserTests.cs` — sequence, реклама и `EXT-X-MAP`.
- `TwitchDownloader2.CLI.Tests/LiveStreamRecorderTests.cs` — dedupe, дыры, retry и token refresh.
- `TwitchDownloader2.CLI.Tests/MediaFinalizerTests.cs` — MP4 validation, failed-artifacts и recovery.
- `TwitchDownloader2.CLI.Tests/TwitchDownloaderServiceTests.cs` — одна сессия на канал, отмена, pause и shutdown.
- `TwitchDownloader2.CLI.Tests/TwitchCheckerServiceTests.cs` — scheduler и `ForceCheck` без обхода pause.
- `TwitchDownloader2.CLI.Tests/KeyboardsTests.cs` — session-ID callback и защита от устаревшей кнопки.
- `TwitchDownloader2.CLI.Tests/AppSettingsTests.cs` — сериализация state mutation и persistence.

## Запуск

```bash
dotnet test TwitchDownloader.sln
```

## Инварианты

- Тесты не требуют Twitch credentials, Telegram token, `ffmpeg` или Docker.
- Реальные `ffmpeg`/`ffprobe` и Twitch проверяются отдельным smoke-тестом окружения.
- Legacy `TwitchDownloader.CLI` не является тестовой целью этого проекта.

## Зависимости

- Тестирует: [[modules/v2-twitch-playback]], [[modules/v2-media-recording]],
  [[modules/v2-twitch-downloader]], [[modules/v2-twitch-checker]],
  [[modules/v2-telegram-service]], [[modules/v2-app-settings]].
