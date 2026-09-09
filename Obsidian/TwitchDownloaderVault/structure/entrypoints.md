# Точки входа и запуск

Parent: [[Index]]

## Версия 2 — `TwitchDownloader2.CLI/Program.cs`

Класс `Program` в namespace `TwitchDownloader2.CLI`. Статические свойства держат singleton-инстансы сервисов:
- `TelegramServiceInstance`
- `Settings` (загружается через `AppSettings.Load()` при инициализации поля)
- `TwitchChecker`
- `TwitchDownloader`

### Порядок запуска `Main(string[] args)`:
1. Устанавливает UTF-8 для консоли (для корректного отображения эмодзи).
2. `SettingsChecker()` — использует `TELEGRAM_BOT_TOKEN`/`TELEGRAM_OWNER_ID` из env с приоритетом над файлом; при интерактивном запуске запрашивает отсутствующие значения.
3. Создаёт Telegram, shared `HttpClient`, Twitch playback и `TwitchDownloaderService`.
4. Запускает background recovery существующих `.recording`.
5. Создаёт `TwitchCheckerService`, затем запускает Telegram polling и checker worker.
6. `ExitAsync()` ждёт `STOP`/сигнал; на выходе отменяет recovery, checker, HTTP/media-сессии, сохраняет настройки и освобождает ресурсы.

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

См. также [[api/external-tools]] — для v2 обязательны `ffmpeg` и `ffprobe` в PATH.
