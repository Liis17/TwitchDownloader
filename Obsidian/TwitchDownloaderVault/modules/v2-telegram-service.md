# v2: TelegramService

Parent: [[Index]]

## Назначение

Telegram-интерфейс владельца и реализация `IRecordingNotificationSink`. Управляет tracked-
каналами, запускает проверки, показывает recording/finalizing-сессии и запрашивает отмену
строго по UUID.

## Файлы

- `TwitchDownloader2.CLI/TelegramService.cs`

## Ключевые методы

| Метод | Описание |
|-------|----------|
| `Start(): void` / `Stop(): void` | Запускают long polling и отменяют его при shutdown. |
| `HandleUpdateAsync(bot: ITelegramBotClient, update: Update, cancellationToken: CancellationToken): Task` | Отбрасывает чужие апдейты и маршрутизирует message/callback. |
| `HandleCallbackAsync(bot: ITelegramBotClient, callback: CallbackQuery, cancellationToken: CancellationToken): Task` | Разбирает `delete_channel:<channel>` и `stop_download:<sessionId>`; после действия обновляет inline-кнопки исходного сообщения. |
| `SendHistoryAsync` / `FormatHistoryEntry` | Показывают последние 10 записей из [[modules/v2-recording-history]]: дата, начало–конец, общая длительность и длительность видео, статус, размер, реклама, дыры, MP4 или причина ошибки. |
| `RecordingStartedAsync(info: RecordingStartedInfo, cancellationToken: CancellationToken): Task` | Показывает канал, качество и путь активного `.recording`. |
| `RecordingEndedAsync(info: RecordingEndedInfo, cancellationToken: CancellationToken): Task` | Сразу сообщает о естественном завершении эфира и начале обработки. |
| `RecordingCompletedAsync(info: RecordingCompletionInfo, cancellationToken: CancellationToken): Task` | Сообщает о завершении обработки; показывает длительность, размер, рекламу и дыры, при успехе — MP4, при ошибке — `.failed`. |
| `SendMessageAsync(text: string, replyMarkup: ReplyMarkup?, cancellationToken: CancellationToken, parseMode: ParseMode): Task` | Отправляет сообщение владельцу с выключенным link preview. |
| `ExtractChannelName(input: string): string` | Выделяет имя из URL/текста до `/`, `?` или `&`. |

## Отмена записи

1. Кнопка `⛔ Остановить запись` находится в главном меню.
2. Бот получает `ActiveDownloadInfo` и отдельным сообщением присылает inline-кнопку на каждую сессию.
   После нажатия кнопки этого сообщения обновляются по текущему списку сессий.
3. Callback содержит session ID, а не имя канала.
4. После `Accepted` бот сразу пишет: «Запись остановлена, идёт сборка MP4».
5. Устаревший callback получает `NotFound` и не затрагивает replacement-сессию.
6. Если pause нельзя сохранить, callback сообщает об ошибке, а recorder продолжает работу.

Естественное завершение (`EndList` или подтверждённый `Offline`) даёт отдельное уведомление
`RecordingEndedAsync` до результата финализации. Обработка запускается параллельно с отправкой
этого уведомления и не блокирует новую запись того же канала. Ручная отмена не отправляет
уведомление о естественном завершении эфира.

## Ручная запись и загрузка VOD

`⬇️ Загрузить` ждёт одну ссылку (`_downloadTrigger`). `TryParseDownloadTarget` отличает VOD
(`/videos/<id>`, `/<channel>/video/<id>`, номер) от канала. VOD передаётся в
`TryStartVodDownloadAsync`, канал — в `TryStartManualRecordingAsync`. Уведомления о старте и
завершении, кнопки остановки, `📜 Статус` (с процентом скачанных сегментов VOD) и
`🏺 История` различают тип по `RecordingKind`.

## Каналы

`📺 Каналы` показывает список и меню каналов (`➕ Добавить`, `🗑️ Удалить`, возврат на главную).
`🗑️ Удалить` присылает отдельное сообщение с inline-кнопкой на каждый канал (`delete_channel:<channel>`);
после удаления кнопки сообщения обновляются по оставшимся каналам.
Удаление проходит через `AppSettings.RemoveTrackedChannel`, поэтому одновременно снимает
persisted-паузу канала. Булевы триггеры остались только для добавления канала и пути.

## Зависимости

- Использует: [[modules/v2-keyboards]], [[modules/v2-app-settings]], [[modules/v2-recording-history]],
  [[modules/v2-twitch-checker]], [[modules/v2-twitch-downloader]].
- Создаётся в: [[modules/v2-program]].
