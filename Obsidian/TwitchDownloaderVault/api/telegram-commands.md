# Telegram-команды (v2)

Parent: [[index]]

Все команды обрабатываются в `HandleUpdateAsync` файла `TwitchDownloader2.CLI/TelegramService.cs`. Доступ только для пользователя с `Id == Program.Settings.TelegramIdOwner`.

## Текстовые команды

| Команда | Действие |
|---------|---------|
| `/start` | Приветствие + `MainPageString` (uptime, кол-во каналов), клавиатура [[modules/v2-keyboards]] `GetMainKeyboard` |
| `🏠 Главная` / `🏠 Вернуться на главную` | То же что `/start` |

## Главное меню (reply-кнопки)

| Кнопка | Действие |
|--------|---------|
| `📺 Каналы` | Список `TrackedChannels` со ссылками `https://www.twitch.tv/{name}` |
| `➕ Добавить` | Включает `_addChannelTrigger`, клавиатура с одной кнопкой "Отменить действие". Следующий текст пользователя добавляется в `TrackedChannels` после `ExtractChannelName().ToLower()` |
| `🗑️ Удалить` | Включает `_deleteChannelTrigger`, динамическая клавиатура из текущих каналов. Следующий текст удаляется из `TrackedChannels` |
| `📜 Статус` | `TwitchChecker.GetStatuses()` → таблица каналов с эмодзи 🔴 (live) / 💤 (idle) |
| `🏺 История` | **Не обработано**, упадёт в default → "Нет такой команды" |
| `⬇️ Загрузить` | Показывает download-клавиатуру (заглушка, идентична main) |
| `⚙ Настройки` | Открывает settings-клавиатуру |
| `🔁 Принудительно обновить` | `Program.TwitchChecker.ForceCheck()` |
| `❌ Отменить действие` | `disableTriggers()` + сообщение об отмене |

## Меню настроек (`⚙ Настройки`)

| Кнопка | Действие |
|--------|---------|
| `📂 Папка загрузки` | Показывает текущий путь в MarkdownV2 + inline-кнопка "Изменить" |
| `💾 Сохранить настройки` | `Program.Settings.Save()` + сообщение об успехе |
| `[placeholder]` | Заглушка: "Действие еще не реализованно..." со ссылкой на (несуществующий) сайт |

## Inline callback (`CallbackQuery.Data`)

| Data | Действие |
|------|---------|
| `info` | "Это информация о сервисе 🧠" |
| `settings` | "Здесь будут настройки ⚙" |
| `editdownloadpath` | Включает `_editDownloadPathTrigger`, просит ввести путь. Дальше: если `Directory.Exists(text)` → меняет `DownloadPath` и сохраняет |

## Многошаговые состояния (триггеры)

Каждый триггер — `bool`, активируется на одну следующую отправку текста, сбрасывается через `disableTriggers()`:
- `_addChannelTrigger` — ожидаем имя/url канала к добавлению
- `_deleteChannelTrigger` — ожидаем имя канала к удалению
- `_editDownloadPathTrigger` — ожидаем путь к новой папке

См. также [[modules/v2-telegram-service]].
