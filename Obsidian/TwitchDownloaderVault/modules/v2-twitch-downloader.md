# v2: TwitchDownloaderService

Parent: [[index]]

## Назначение
Запускает параллельную ffmpeg-сессию для скачивания live-стрима: 2 видеопотока + 2 аудиопотока в отдельных окнах cmd. После завершения сравнивает файлы по SHA256 и удаляет дубликаты. Шлёт уведомления в Telegram через [[modules/v2-telegram-service]].

## Файлы
- `TwitchDownloader2.CLI/TwitchDownloaderService.cs`

## Состояние
| Поле | Назначение |
|------|-----------|
| `_downloadRoot` | Корневая папка для файлов; из конструктора или дефолт `{BaseDirectory}/Downloads` |
| `_rng` | `Random` для генерации `sessionCode` |

## Ключевые методы
| Метод | Описание |
|-------|---------|
| `StartDownload(channelName)` | Async-метод: генерирует sessionCode → шлёт уведомления в Telegram → резолвит HLS → стартует фоновый поток `RunDownloadSession` |
| `RunDownloadSession(channel, sessionCode, hlsUrl, fv1, fv2, fa1, fa2)` | Запускает 4 ffmpeg-процесса (cmd.exe `/c start ... /WAIT ffmpeg ...`), ждёт всех, проверяет хеши, удаляет дубликаты, помечает завершение |
| `static FilesEqualByHash(p1, p2)` | SHA256-сравнение двух файлов |
| `static SafeDelete(path)` | `File.Delete` с проглатыванием исключений |
| `ResolveHlsUrl(channel)` | `yt-dlp --no-warnings --get-url https://www.twitch.tv/{channel}` → первая строка stdout |
| `StartFfmpegInNewWindow(title, args)` | Запуск `cmd.exe /c start "title" /WAIT ffmpeg <args>` |
| `GenerateCode(len)` | Случайная строка из `[a-zA-Z0-9]` |

## Структура файлов сессии
```
{channel}_video_1_{sessionCode}.ts
{channel}_video_2_{sessionCode}.ts
{channel}_audio_1_{sessionCode}.aac
{channel}_audio_2_{sessionCode}.aac
```

## ffmpeg-параметры
- Общие: `-hide_banner -loglevel warning -y -reconnect 1 -reconnect_streamed 1 -reconnect_at_eof 1 -reconnect_on_network_error 1 -reconnect_delay_max 10`
- Video: `-c copy -f mpegts`
- Audio: `-vn -c:a aac -b:a 160k -f adts`

## Зависимости
- Использует: `ffmpeg`, `yt-dlp` (см. [[api/external-tools]]), [[modules/v2-telegram-service]] (`Program.TelegramServiceInstance.SendNotification`), [[modules/v2-twitch-checker]] (`MarkDownloadFinished`)
- Используется в: [[modules/v2-program]] (создаётся в `Main`), [[modules/v2-twitch-checker]] (вызов `StartDownload`)

## Важные детали
- **Окна cmd видимы** (`CreateNoWindow = false`) — каждый ffmpeg запускается в отдельном окне с заголовком `[TD2] {channel} video #N`.
- Заявлен `async Task StartDownload`, но внутри запускается обычный фоновый `Thread` для `RunDownloadSession` — после `Thread.Start()` метод возвращается. Уведомление об окончании отправляется из самого потока.
- `Thread.Sleep(1000)` между двумя уведомлениями в `StartDownload` — задержка для предотвращения склейки сообщений Telegram'ом.
- Дедупликация постфактум: оба видеопотока скачиваются одинаковые → один удаляется. Сделано на случай разрывов и невозможности докачать середину одного из дублей.
- В `StartDownload` пути для текста уведомления берутся из `Program.Settings.DownloadPath`, а реальные пути файлов — из `_downloadRoot`. Если их сменить через бота между запусками, тексты могут разойтись с реальностью.
