# v2: TelegramService

Parent: [[Index]]

## Назначение

Telegram-интерфейс владельца и реализация `IRecordingNotificationSink`. Управляет tracked-
каналами, запускает проверки, показывает active-сессии и запрашивает отмену строго по UUID.

## Файлы

- `TwitchDownloader2.CLI/TelegramService.cs`

## Ключевые методы

| Метод | Описание |
|-------|----------|
| `Start(): void` / `Stop(): void` | Запускают long polling и отменяют его при shutdown. |
| `HandleUpdateAsync(bot, update, cancellationToken): Task` | Отбрасывает чужие апдейты и маршрутизирует message/callback. |
| `HandleCallbackAsync(bot, callback, cancellationToken): Task` | Разбирает `stop_download:<sessionId>` и вызывает `RequestStopAsync`. |
| `RecordingStartedAsync(info, cancellationToken): Task` | Показывает канал, качество и путь активного `.recording`. |
| `RecordingCompletedAsync(info, cancellationToken): Task` | Для успеха показывает MP4, длительность, размер, рекламу и дыры; для ошибки — сохранённые `.failed`. |
| `SendMessageAsync(text, replyMarkup, cancellationToken, parseMode): Task` | Отправляет сообщение владельцу с выключенным link preview. |
| `ExtractChannelName(input): string` | Выделяет имя из URL/текста до `/`, `?` или `&`. |

## Отмена записи

1. Кнопка `⛔ Остановить запись` находится в главном меню.
2. Бот получает `ActiveDownloadInfo` и строит inline-кнопку на каждую сессию.
3. Callback содержит session ID, а не имя канала.
4. После `Accepted` бот сразу пишет: «Запись остановлена, идёт сборка MP4».
5. Устаревший callback получает `NotFound` и не затрагивает replacement-сессию.

Удаление tracked-канала проходит через `AppSettings.RemoveTrackedChannel`, поэтому одновременно
снимает его persisted-паузу. Булевы триггеры остались только для добавления, удаления и пути.

## Зависимости

- Использует: [[modules/v2-keyboards]], [[modules/v2-app-settings]],
  [[modules/v2-twitch-checker]], [[modules/v2-twitch-downloader]].
- Создаётся в: [[modules/v2-program]].
