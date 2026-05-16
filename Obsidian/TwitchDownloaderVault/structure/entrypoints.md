# Точки входа и запуск

Parent: [[index]]

## Версия 2 — `TwitchDownloader2.CLI/Program.cs`

Класс `Program` в namespace `TwitchDownloader2.CLI`. Статические свойства держат singleton-инстансы сервисов:
- `TelegramServiceInstance`
- `Settings` (загружается через `AppSettings.Load()` при инициализации поля)
- `TwitchChecker`
- `TwitchDownloader`

### Порядок запуска `Main(string[] args)`:
1. Устанавливает UTF-8 для консоли (для корректного отображения эмодзи).
2. `SettingsChecker()` — интерактивно запрашивает `TelegramToken` и `TelegramIdOwner`, если их нет в настройках.
3. Создаёт и запускает `TelegramService` (см. [[modules/v2-telegram-service]]).
4. Создаёт `TwitchDownloaderService(Settings.DownloadPath)` (см. [[modules/v2-twitch-downloader]]).
5. Создаёт `TwitchCheckerService()` — стартует фоновый поток мониторинга (см. [[modules/v2-twitch-checker]]).
6. `Exit()` — цикл `Console.ReadLine()`, выход по слову `STOP`. На выходе сохраняет настройки и останавливает Telegram.

### Логирование
Все сервисы используют собственный приватный `ConsoleWriteLine(message, color)` с префиксом `[ServiceName]` в своём цвете. Это копипаста, не общий хелпер.

## Версия 1 — `TwitchDownloader.CLI/Program.cs`

Класс `Program` без namespace. Статические поля:
- `downloadService`
- `telegramService`
- `converterService`

### Порядок запуска `Main(string[] args)`:
1. `IsAdministrator()` — обязательная проверка прав, иначе выход.
2. `savePath` берётся из `args[0]` или остаётся пустым (DownloadService подставит `%USERPROFILE%/Downloads`).
3. Создаются сервисы: `DownloadService`, `TelegramService`, `ConverterService`.
4. Читаются файлы `token` и `id` (без обработки ошибок).
5. `telegramService.StartBotAsync(token, adminId).Wait()` — синхронное ожидание запуска бота.
6. `Thread.Sleep(Timeout.Infinite)` — главный поток спит вечно.

## Запуск из командной строки

```bash
# v1 (требует admin)
TwitchDownloader.CLI.exe [путь_для_сохранения]

# v2
TwitchDownloader2.CLI.exe
```

См. также [[api/external-tools]] — обязательные `ffmpeg` и `yt-dlp` в PATH.
