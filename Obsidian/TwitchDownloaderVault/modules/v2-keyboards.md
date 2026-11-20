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
| `GetMainKeyboard(placeholder: string): ReplyKeyboardMarkup` | Reply | Главное меню: каналы, статус, история, загрузить, остановка записи, настройки. |
| `GetChannelsKeyboard(): ReplyKeyboardMarkup` | Reply | Меню каналов: добавить, удалить, вернуться на главную. |
| `GetDeleteChannelsKeyboard(channels: IEnumerable<string>): InlineKeyboardMarkup` | Inline | Одна кнопка на канал с callback `delete_channel:<channel>`. |
| `CreateDeleteChannelCallback` / `TryParseDeleteChannelCallback` | string / bool | Единый codec `delete_channel:<channel>`. |
| `GetOnlyCancelKeyboard(placeholder: string): ReplyKeyboardMarkup` | Reply | Одна кнопка "❌ Отменить действие". |
| `GetServiceKeyboard(placeholder: string): ReplyKeyboardMarkup` | Reply | Main + Cancel сверху. |
| `GetSettingsKeyboard(): ReplyKeyboardMarkup` | Reply | Папка, сохранение и возврат. |
| `GetStopDownloadsKeyboard(downloads: IEnumerable<ActiveDownloadInfo>): InlineKeyboardMarkup` | Inline | Одна callback-кнопка на active-сессию. |
| `CreateStopDownloadCallback(sessionId: string): string` / `TryParseStopDownloadCallback(callbackData: string?, sessionId: out string): bool` | string / bool | Единый codec `stop_download:<sessionId>`. |
| `GetPathEditKeyboard(): ReplyKeyboardMarkup` | Reply | Одна кнопка возврата на главную. |

## Зависимости
- Используется в: [[modules/v2-telegram-service]] — все отправки сообщений с клавиатурами

## Важные детали
- Все клавиатуры — `IsPersistent = true, ResizeKeyboard = true, OneTimeKeyboard = false`.
- `⬇️ Загрузить` ждёт ссылку под клавиатурой `GetOnlyCancelKeyboard` (см. [[api/telegram-commands]]).
- Кнопка остановки загрузки VOD подписана номером VOD и каналом.
- Session ID помещается в callback вместо имени канала; итоговая строка укладывается в лимит Telegram callback data.
  Имя канала (до 25 символов) в `delete_channel:` тоже укладывается в лимит 64 байта.
