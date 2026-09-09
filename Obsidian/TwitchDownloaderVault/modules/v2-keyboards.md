# v2: Keyboards

Parent: [[Index]]

## Назначение
Фабрика всех reply/inline клавиатур Telegram-бота v2. Статические методы возвращают готовые `ReplyKeyboardMarkup` / `InlineKeyboardMarkup`.

## Файлы
- `TwitchDownloader2.CLI/Keyboards.cs`

## Методы
| Метод | Тип возврата | Описание |
|-------|-------------|---------|
| `GetEditPathButton(): InlineKeyboardMarkup` | Inline | Одна кнопка "🖊️ Изменить" с callback `editdownloadpath`. |
| `GetMainKeyboard(placeholder: string): ReplyKeyboardMarkup` | Reply | Главное меню, включая `⛔ Остановить запись`. |
| `GetDownloadKeyboard(placeholder: string): ReplyKeyboardMarkup` | Reply | Идентична main; задел для download-меню. |
| `GetOnlyCancelKeyboard(placeholder: string): ReplyKeyboardMarkup` | Reply | Одна кнопка "❌ Отменить действие". |
| `GetServiceKeyboard(placeholder: string): ReplyKeyboardMarkup` | Reply | Main + Cancel сверху. |
| `GetDynamicKeyboard(items: IEnumerable<string>, placeholder: string): ReplyKeyboardMarkup` | Reply | До четырёх элементов в ряд + Cancel. |
| `GetSettingsKeyboard(): ReplyKeyboardMarkup` | Reply | Папка, сохранение, плейсхолдеры и возврат. |
| `GetStopDownloadsKeyboard(downloads: IEnumerable<ActiveDownloadInfo>): InlineKeyboardMarkup` | Inline | Одна callback-кнопка на active-сессию. |
| `CreateStopDownloadCallback(sessionId: string): string` / `TryParseStopDownloadCallback(callbackData: string?, sessionId: out string): bool` | string / bool | Единый codec `stop_download:<sessionId>`. |
| `GetPathEditKeyboard(): ReplyKeyboardMarkup` | Reply | Одна кнопка возврата на главную. |

## Зависимости
- Используется в: [[modules/v2-telegram-service]] — все отправки сообщений с клавиатурами

## Важные детали
- Все клавиатуры — `IsPersistent = true, ResizeKeyboard = true, OneTimeKeyboard = false`.
- `GetDownloadKeyboard` дублирует `GetMainKeyboard` — задел, но кнопка ⬇️ Загрузить пока не имеет своего меню.
- Кнопки `📜 Статус`, `🏺 История`, `⬇️ Загрузить` — частично заглушки (см. [[api/telegram-commands]]).
- В `GetSettingsKeyboard` несколько `[placeholder]` — обработчик отвечает обычным текстом-заглушкой.
- Session ID помещается в callback вместо имени канала; итоговая строка укладывается в лимит Telegram callback data.
