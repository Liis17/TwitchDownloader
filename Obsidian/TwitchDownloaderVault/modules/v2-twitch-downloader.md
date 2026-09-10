# v2: TwitchDownloaderService

Parent: [[Index]]

## Назначение

Владеет полным жизненным циклом live-записи. Для одного канала допускает ровно одну
recording-сессию, при этом финализация предыдущей записи не блокирует новую live-сессию.
Разные каналы также записываются параллельно. Checker и Telegram работают только с
публичным интерфейсом сессий и не управляют HTTP/file/ffmpeg напрямую.

## Файлы

- `TwitchDownloader2.CLI/TwitchDownloaderService.cs`

## Публичный интерфейс

| Метод | Описание |
|-------|----------|
| `TryStartDownloadAsync(channel: string, cancellationToken: CancellationToken): Task<StartDownloadResult>` | Проверяет active recording/pause/live, резервирует канал и возвращает `Started`, `AlreadyActive`, `Offline`, `Suppressed` либо `Failed`. |
| `RequestStopAsync(sessionId: string, cancellationToken: CancellationToken): Task<StopDownloadResult>` | Сохраняет pause-until-offline, отменяет только указанную запись и сразу возвращает `Accepted`, `AlreadyStopping` либо `NotFound`. Ошибка persistence отклоняет stop исключением. |
| `GetActiveDownloads(): IReadOnlyList<ActiveDownloadInfo>` | Возвращает session ID, канал, UTC-время старта, путь сырья и состояние `Recording`/`Finalizing`. |
| `StopForShutdownAsync(cancellationToken: CancellationToken): Task` | Отменяет HTTP и media tools, закрывает сырьё и намеренно не запускает/не продолжает финализацию. |
| `RecoverInterruptedDownloadsAsync(cancellationToken: CancellationToken): Task<IReadOnlyList<MediaFinalizeResult>>` | Перед стартом checker передаёт orphan `.recording` в [[modules/v2-media-recording]]. |

## Жизненный цикл

1. Прямой [[modules/v2-twitch-playback]] возвращает source media playlist или offline.
2. Под lock создаётся UUID сессии и путь
   `live_<channel>_<timestamp>_source_<sessionId>.recording`.
3. [[modules/v2-media-recording]] пишет HLS в фоне; сессия видна как `Recording`.
4. Естественный конец переводит её в `Finalizing`, отправляет `RecordingEndedAsync`, освобождает
   резерв канала и параллельно запускает сборку MP4; пользовательская отмена переводит сессию
   в `Finalizing` без уведомления о естественном завершении и с сохраняемой pause-until-offline.
5. Пока старая сессия финализируется, checker может создать новую запись того же канала.
6. После проверки MP4 Telegram получает `RecordingCompletedAsync` с итоговым путём,
   длительностью, размером, рекламой и дырами; затем сессия удаляется.

## Защита повторного старта

- `_sessionsByChannel` — единственный источник истины о recording-сессиях; после естественного
  конца `Finalizing` остаётся только в `_sessionsById`, а manual/error-сессия удерживает резерв
  до очистки worker-а. Checker не дублирует флаги.
- Пользовательский stop сначала добавляет канал в
  `AppSettings.PausedUntilOfflineChannels` и сохраняет настройки, затем отменяет recorder.
- Live-ответ для paused-канала даёт `Suppressed`; ошибка сети даёт `Failed`, но паузу не снимает.
- Только подтверждённый `Offline` удаляет паузу и сохраняет настройки.
- Stop адресуется UUID, поэтому callback старой трансляции не может остановить новую.

## Зависимости

- Использует: [[modules/v2-app-settings]], [[modules/v2-twitch-playback]],
  [[modules/v2-media-recording]], `IRecordingNotificationSink`.
- Используется в: [[modules/v2-program]], [[modules/v2-twitch-checker]],
  [[modules/v2-telegram-service]].

## Удалённый API старой схемы

- ~~`StartDownload(channelName: string): Task`~~ (удалён: 2026-09-10) — заменён на ожидаемый `TryStartDownloadAsync`.
- ~~`StopDownload(channel: string): bool`~~ (удалён: 2026-09-10) — заменён на адресный `RequestStopAsync(sessionId)`.
- ~~`ResolveHlsUrl(channel: string): string`~~ (удалён: 2026-09-10) — вместо `yt-dlp` используется playback client.
- ~~`StartFfmpegProcess(...)` / `FilesEqualByHash(...)`~~ (удалены: 2026-09-10) — четыре потока и SHA256-дедупликация больше не нужны.
