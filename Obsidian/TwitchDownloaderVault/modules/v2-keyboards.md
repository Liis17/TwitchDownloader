# v2: Keyboards

Parent: [[index]]

## Назначение
Фабрика всех reply/inline клавиатур Telegram-бота v2. Статические методы возвращают готовые `ReplyKeyboardMarkup` / `InlineKeyboardMarkup`.

## Файлы
- `TwitchDownloader2.CLI/Keyboards.cs`

## Методы
| Метод | Тип возврата | Описание |
|-------|-------------|---------|
| `GetEditPathButton()` | Inline | Одна кнопка "🖊️ Изменить" с callback `editdownloadpath` |
| `GetMainKeyboard(placeholder)` | Reply | Главное меню: Каналы / Добавить / Удалить, Статус / История / Загрузить, Главная / Настройки, Принудительно обновить |
| `GetDownloadKeyboard(placeholder)` | Reply | Идентична main (заглушка для будущего меню загрузки) |
| `GetOnlyCancelKeyboard(placeholder)` | Reply | Одна кнопка "❌ Отменить действие" |
| `GetServiceKeyboard(placeholder)` | Reply | Main + Cancel сверху (для служебных режимов) |
| `GetDynamicKeyboard(items, placeholder)` | Reply | Динамика: 4 кнопки/ряд из `items` + Cancel сверху. Используется для выбора канала к удалению |
| `GetSettingsKeyboard()` | Reply | Папка загрузки / Сохранить настройки + плейсхолдеры + Вернуться на главную |
| `GetPathEditKeyboard()` | Reply | Одна кнопка "🏠 Вернуться на главную" |

## Зависимости
- Используется в: [[modules/v2-telegram-service]] — все отправки сообщений с клавиатурами

## Важные детали
- Все клавиатуры — `IsPersistent = true, ResizeKeyboard = true, OneTimeKeyboard = false`.
- `GetDownloadKeyboard` дублирует `GetMainKeyboard` — задел, но кнопка ⬇️ Загрузить пока не имеет своего меню.
- Кнопки `📜 Статус`, `🏺 История`, `⬇️ Загрузить` — частично заглушки (см. [[api/telegram-commands]]).
- В `GetSettingsKeyboard` несколько `[placeholder]` — обработчик в Telegram выдаёт заглушечный ответ со ссылкой на сайт.
