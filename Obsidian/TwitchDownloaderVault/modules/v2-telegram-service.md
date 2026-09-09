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
| `HandleUpdateAsync(bot: ITelegramBotClient, update: Update, cancellationToken: CancellationToken): Task` | Отбрасывает чужие апдейты и маршрутизирует message/callback. |
| `HandleCallbackAsync(bot: ITelegramBotClient, callback: CallbackQuery, cancellationToken: CancellationToken): Task` | Разбирает `stop_download:<sessionId>` и вызывает `RequestStopAsync`. |
| `RecordingStartedAsync(info: RecordingStartedInfo, cancellationToken: CancellationToken): Task` | Показывает канал, качество и путь активного `.recording`. |
| `RecordingCompletedAsync(info: RecordingCompletionInfo, cancellationToken: CancellationToken): Task` | Всегда показывает длительность, размер, рекламу и дыры; при успехе — MP4, при ошибке — `.failed`. |
| `SendMessageAsync(text: string, replyMarkup: ReplyMarkup?, cancellationToken: CancellationToken, parseMode: ParseMode): Task` | Отправляет сообщение владельцу с выключенным link preview. |
| `ExtractChannelName(input: string): string` | Выделяет имя из URL/текста до `/`, `?` или `&`. |

## Отмена записи

1. Кнопка `⛔ Остановить запись` находится в главном меню.
2. Бот получает `ActiveDownloadInfo` и строит inline-кнопку на каждую сессию.
3. Callback содержит session ID, а не имя канала.
4. После `Accepted` бот сразу пишет: «Запись остановлена, идёт сборка MP4».
5. Устаревший callback получает `NotFound` и не затрагивает replacement-сессию.
6. Если pause нельзя сохранить, callback сообщает об ошибке, а recorder продолжает работу.

Удаление tracked-канала проходит через `AppSettings.RemoveTrackedChannel`, поэтому одновременно
снимает его persisted-паузу. Булевы триггеры остались только для добавления, удаления и пути.
~~`_stopDownloadTrigger`~~ удалён 2026-09-10: выбор канала текстом заменён inline session callback.

## Зависимости

- Использует: [[modules/v2-keyboards]], [[modules/v2-app-settings]],
  [[modules/v2-twitch-checker]], [[modules/v2-twitch-downloader]].
- Создаётся в: [[modules/v2-program]].
