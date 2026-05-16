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
| `_activeSessions` | `ConcurrentDictionary<string, DownloadSession>` (case-insensitive) — текущие активные загрузки по имени канала |

### Внутренний `DownloadSession`
- `Channel: string`
- `SessionCode: string`
- `Processes: List<Process>` (заполняется в `RunDownloadSession`, под `ProcessesLock`)
- `ProcessesLock: object`
- `ForcedByUser: volatile bool` — пользователь нажал «⛔ Завершить загрузку»

## Ключевые методы
| Метод | Описание |
|-------|---------|
| `StartDownload(channelName)` | Async-метод: генерирует sessionCode → шлёт уведомления в Telegram → резолвит HLS → регистрирует `DownloadSession` в `_activeSessions` → стартует фоновый поток `RunDownloadSession` |
| `GetActiveDownloads()` | Возвращает `IReadOnlyList<string>` с именами каналов, у которых сейчас активная сессия. Используется кнопкой Telegram «⛔ Завершить загрузку» |
| `StopDownload(channel)` | Помечает сессию `ForcedByUser=true` и `Kill(entireProcessTree:true)` для всех процессов. Возвращает `false` если активной сессии нет |
| `RunDownloadSession(session, hlsUrl, fv1, fv2, fa1, fa2)` | Запускает 4 ffmpeg-процесса, `WaitAny` → 10-сек таймер на остальных → при необходимости Kill, проверяет хеши, удаляет дубликаты, ветвится по типу завершения и шлёт уведомление |
| `static FilesEqualByHash(p1, p2)` | SHA256-сравнение двух файлов |
| `static SafeDelete(path)` | `File.Delete` с проглатыванием исключений |
| `ResolveHlsUrl(channel)` | `yt-dlp --no-warnings --get-url https://www.twitch.tv/{channel}` → первая строка stdout |
| `StartFfmpegInNewWindow(title, args)` | Запуск `cmd.exe /c start "title" /WAIT ffmpeg <args>` |
| `GenerateCode(len)` | Случайная строка из `[a-zA-Z0-9]` |

## Логика завершения сессии
1. `Task.WaitAny(waitTasks)` — ждём, пока хоть один ffmpeg закроется.
2. `Task.WaitAll(waitTasks, 10s)` — даём оставшимся 10 секунд на самозакрытие. При штатном завершении стрима все четверо закрываются почти одновременно.
3. Если по таймауту кто-то ещё жив — все процессы убиваются через `Process.Kill(entireProcessTree: true)`. `Process.Kill` работает корректно даже при цепочке `cmd.exe /c start /WAIT ffmpeg` — `start` сохраняет parent-child связь, дерево обходится по PPID.
4. В `finally` — три возможных уведомления в Telegram:

| Состояние | Сообщение | `ForceCheck`? |
|-----------|-----------|---------------|
| `session.ForcedByUser` | «⛔ Загрузка стрима <b>{channel}</b> принудительно приостановлена» | нет |
| `abnormalTermination` | «⚠️ При завершении загрузки <b>{channel}</b> один из ffmpeg-процессов был принудительно закрыт. Канал будет проверен заново.» | **да** |
| штатно | «Загрузка стрима {channel} Завершена» | нет |

После любого исхода — `_activeSessions.TryRemove(channel)` и `MarkDownloadFinished(channel)`.

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
- 10-секундный таймер на самозакрытие — workaround для случаев, когда один ffmpeg теряет HLS и закрывается, а другие зависают с открытым соединением без данных. Без таймера канал оставался помеченным как «загружается» навсегда.
- При `ForcedByUser` повторная проверка не запускается — пользователь сам решил прервать. При `abnormalTermination` вызывается `ForceCheck()`, чтобы checker сразу проверил живость и при положительном результате стартанул новую сессию (с новым `sessionCode`).
