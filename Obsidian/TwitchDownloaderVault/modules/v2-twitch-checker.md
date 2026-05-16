# v2: TwitchCheckerService

Parent: [[index]]

## Назначение
Фоновый сервис, который раз в минуту опрашивает все каналы из `Program.Settings.TrackedChannels`, определяет идёт ли live, и при обнаружении стрима — инициирует загрузку через [[modules/v2-twitch-downloader]]. Поддерживает принудительный сброс ожидания через `ForceCheck()`.

## Файлы
- `TwitchDownloader2.CLI/TwitchCheckerService.cs`

## Внутреннее состояние
| Поле | Назначение |
|------|-----------|
| `_workerThread` | Background thread с циклом `WorkerLoop` |
| `_cts` | Отмена работы (используется в Dispose) |
| `_forceCheckEvent` | `AutoResetEvent`, сигнализирует о принудительной проверке |
| `_checkInterval` | `TimeSpan.FromMinutes(1)` — период между плановыми проверками |
| `_channelStates` | `Dictionary<string, ChannelDownloadState>` (case-insensitive) |
| `_stateLock` | Lock для синхронизации доступа к `_channelStates` |

### Внутренний `ChannelDownloadState`
- `Channel: string`
- `IsDownloading: bool`
- `Pids: List<int>` (заявлено, но не используется активно)

## Ключевые методы
| Метод | Описание |
|-------|---------|
| `ForceCheck()` | `_forceCheckEvent.Set()` — сбрасывает ожидание и запускает проверку немедленно |
| `MarkDownloadFinished(channel)` | Сбрасывает `IsDownloading=false` и чистит `Pids` |
| `GetStatuses()` | Возвращает `IReadOnlyDictionary<string, bool>` по всем `TrackedChannels` |
| `WorkerLoop()` | Главный цикл: 2 сек pause → перебор каналов → ожидание (`WaitAny` cts/forceCheck/timeout) |
| `IsDownloading(channel)` | Под локом проверяет state |
| `TryMarkDownloadStarted(channel)` | Атомарная пометка о старте; возвращает `false` если уже идёт |
| `IsChannelLive(channel, token)` | Запускает `yt-dlp --quiet --no-warnings --print title <url>` с таймаутом 30 сек. Возврат `ExitCode == 0` |
| `Dispose()` | Отмена, сигнал events, ожидание потока до 3 сек, иначе `Interrupt()` |

## Зависимости
- Использует: `yt-dlp` (внешний CLI, см. [[api/external-tools]]), [[modules/v2-app-settings]] (`Program.Settings.TrackedChannels`), [[modules/v2-twitch-downloader]] (`Program.TwitchDownloader.StartDownload`)
- Используется в: [[modules/v2-program]] (создаётся в `Main`), [[modules/v2-telegram-service]] (`Program.TwitchChecker.ForceCheck()`, `GetStatuses()`)

## Важные детали
- Поток стартует **сразу** в конструкторе, до окончания `Main`.
- 2-секундная пауза в начале `WorkerLoop` — workaround на гонку с инициализацией остальных сервисов.
- Если `Program.TwitchDownloader == null` на момент обнаружения live — отметка сбрасывается и попытка повторяется в следующем цикле.
- `WaitAny` принимает массив `[cts.WaitHandle, _forceCheckEvent]` + timeout. Индекс 0 — отмена (break), 1 — force (продолжаем), timeout — обычная итерация.
- `Pids` в `ChannelDownloadState` объявлены, но никем не заполняются — задел на будущее.
