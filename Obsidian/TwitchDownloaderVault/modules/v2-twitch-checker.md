# v2: TwitchCheckerService

Parent: [[Index]]

## Назначение

Лёгкий async-планировщик. Раз в минуту передаёт снимок отслеживаемых каналов в
`ITwitchDownloaderService.TryStartDownloadAsync`; сам больше не проверяет Twitch через
`yt-dlp` и не хранит `IsDownloading`, PID или отметки завершения.

## Файлы

- `TwitchDownloader2.CLI/TwitchCheckerService.cs`

## Ключевые методы

| Метод | Описание |
|-------|----------|
| `Start(): void` | Один раз запускает async worker после сборки всех зависимостей. |
| `ForceCheck(): void` | Кладёт единичный сигнал в `SemaphoreSlim`; серия кликов схлопывается. |
| `CheckNowAsync(cancellationToken: CancellationToken): Task` | Параллельно вызывает downloader для уникальных tracked-каналов. |
| `GetStatuses(): IReadOnlyDictionary<string, bool>` | Строит статус из `GetActiveDownloads()`, не из локальной копии состояния. |
| `StopAsync(cancellationToken: CancellationToken): Task` | Отменяет и ожидает worker. |
| `Dispose(): void` | Идемпотентно отменяет worker и освобождает примитивы синхронизации. |

## Важные детали

- `ForceCheck` меняет только расписание. Он не передаёт downloader флаг обхода и потому
  не может снять pause-until-offline.
- Разные каналы проверяются через `Task.WhenAll`; правило одной сессии обеспечивает сам downloader.
- Сетевая ошибка локализована результатом `Failed` и не останавливает следующий цикл.

## Зависимости

- Использует: [[modules/v2-app-settings]], [[modules/v2-twitch-downloader]].
- Используется в: [[modules/v2-program]], [[modules/v2-telegram-service]].

## Удалённый API старой схемы

- ~~`MarkDownloadFinished(channel: string): void`~~ (удалён: 2026-09-10) — active-state принадлежит downloader.
- ~~`IsChannelLive(channel: string, token: CancellationToken): bool`~~ (удалён: 2026-09-10) — live/offline возвращает playback client внутри downloader.
- ~~`TryMarkDownloadStarted(channel: string): bool`~~ (удалён: 2026-09-10) — атомарность обеспечивает session registry downloader.
