# v2: RecordingHistory

Parent: [[Index]]

## Назначение

История записей: итог каждой завершённой записи сохраняется в локальную SQLite-базу и
показывается владельцу по кнопке `🏺 История` (см. [[api/telegram-commands]]).

## Файлы

- `TwitchDownloader2.CLI/RecordingHistory.cs` — `RecordingHistoryStore`, `RecordingHistoryEntry`,
  `RecordingHistoryNotificationSink`.
- Runtime: `Data/history.db` (`RecordingHistoryStore.DefaultDatabasePath`), рядом с `settings.data`;
  в Docker попадает в volume `docker/data/`.

## Контракт

- Пакет `Microsoft.Data.Sqlite`; таблица `recordings` создаётся при открытии (`CREATE TABLE IF NOT EXISTS`).
  Ключ — UUID сессии, повторное сохранение той же сессии заменяет строку.
- Строка содержит тип (`kind`: `Tracked`/`Manual`/`Vod`), номер VOD, канал, начало записи, конец
  записи (момент остановки захвата HLS; у VOD — конец скачивания сегментов), момент окончания
  финализации, успех, признак ручной остановки, длительность видео, размер, пути к сырью
  и MP4, рекламу, дыры и текст ошибки.
- Колонки `kind` и `vod_id` добавляются в базу старой версии через `ALTER TABLE` при открытии;
  старые строки получают `Tracked`.
- Даты хранятся строками в UTC единого формата, поэтому сортировка по `started_at` хронологическая.
  В Telegram время показывается в часовом поясе процесса (`ToLocalTime`; в контейнере — как задан `TZ`).
- `RecordingHistoryNotificationSink` — декоратор `IRecordingNotificationSink`: в
  `RecordingCompletedAsync` пишет строку и затем передаёт уведомление в `TelegramService`.
  Ошибка записи в базу только логируется и не мешает уведомлению владельца.
- Данные для строки приходят в `RecordingCompletionInfo` (`StartedAt`, `RecordingEndedAt`,
  `StoppedManually`, `Kind`, `VodId`) от [[modules/v2-twitch-downloader]]. Для VOD Telegram
  показывает начало и конец загрузки и сколько она скачивалась.

## Ограничения

- В историю попадают записи, для которых отправлено уведомление о завершении обработки.
  Сессии, прерванные остановкой приложения, и файлы, финализированные восстановлением при
  старте, в историю не записываются.

## Зависимости

- Создаётся в [[modules/v2-program]] (`Program.History`), читается в [[modules/v2-telegram-service]].
