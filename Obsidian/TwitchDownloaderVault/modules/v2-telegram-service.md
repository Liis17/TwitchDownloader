# v2: TelegramService

Parent: [[index]]

## Назначение
Telegram-бот, который обрабатывает сообщения и callback'и от единственного разрешённого пользователя (`Program.Settings.TelegramIdOwner`). Управляет каналами, путём загрузки и инициирует проверки/загрузки. Состояние ввода многошаговых команд хранится в булевых триггерах.

## Файлы
- `TwitchDownloader2.CLI/TelegramService.cs`

## Внутреннее состояние
| Поле | Тип | Назначение |
|------|-----|-----------|
| `_bot` | `TelegramBotClient` | Клиент Telegram.Bot 22.7.4 |
| `_ownerId` | `long` | ID единственного администратора |
| `_cts` | `CancellationTokenSource?` | Для остановки приёма апдейтов |
| `_addChannelTrigger` | `bool` | Ожидаем ли ввод имени для добавления |
| `_deleteChannelTrigger` | `bool` | Ожидаем ли ввод имени для удаления |
| `_editDownloadPathTrigger` | `bool` | Ожидаем ли ввод нового пути |
| `_stopDownloadTrigger` | `bool` | Ожидаем ли выбор канала для принудительного завершения загрузки |

Все триггеры сбрасываются методом `disableTriggers()`.

## Ключевые методы
| Метод | Описание |
|-------|---------|
| `Start()` | Создаёт `_cts` и запускает `RunAsync` в `Task.Run` |
| `Stop()` | Отменяет `_cts` |
| `RunAsync(token)` | Стартует приём апдейтов через `_bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync)` |
| `HandleUpdateAsync(bot, update, token)` | Главный диспетчер: фильтр по `_ownerId`, ветвление по триггерам и тексту кнопок |
| `HandleErrorAsync(...)` | Логирует ошибку Telegram |
| `SendMessageAsync(text, replyMarkup, ct, parseMode)` | Отправка владельцу, по умолчанию HTML, превью ссылок выключено |
| `SendNotification(text)` | Обёртка над `SendMessageAsync` для уведомлений |
| `ExtractChannelName(input)` | Чистит ссылку до имени канала: убирает `https://`, `www.`, `twitch.tv/`, query-параметры |
| `disableTriggers()` | Сброс всех `_*Trigger` в `false` |
| `MainPageString()` | Текст главной страницы (uptime + кол-во каналов) |

## Обрабатываемые команды
См. [[api/telegram-commands]] для полного списка кнопок и их поведения.

## Зависимости
- Использует: [[modules/v2-keyboards]] (`Keyboards.GetMainKeyboard()` и т.д.), [[modules/v2-app-settings]] (`Program.Settings`), [[modules/v2-twitch-checker]] (`Program.TwitchChecker.ForceCheck()`, `GetStatuses()`)
- Используется в: [[modules/v2-program]] (создаётся в `Main`), [[modules/v2-twitch-downloader]] (вызывает `SendNotification` при старте и завершении загрузки)

## Важные детали
- **Многошаговый ввод** реализован через булевы триггеры, а не FSM. После одного шага все триггеры сбрасываются.
- Username отправителя при логировании: если `Username` пустой, используется `Id.ToString()`, но тут есть баг — после этого присваивания строка всё равно перезаписывается на `message.From.Username`.
- `parseMode` по умолчанию — `ParseMode.Html`. Для текстов с экранированными слешами пути используется `MarkdownV2`.
- При удалении канала используется `message.Text.Replace(" ", "")` (без `.ToLower()` и `ExtractChannelName()`), тогда как при добавлении нормализация полная — потенциальная асимметрия.
- На неизвестный текст — `"Нет такой команды: <b>{text}</b>"`.
