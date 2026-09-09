# v2: Program

Parent: [[Index]]

## Назначение
Главный класс приложения v2. Хранит singleton-ссылки на все сервисы через статические свойства, оркестрирует запуск и остановку, проверяет минимальные настройки.

## Файлы
- `TwitchDownloader2.CLI/Program.cs` — единственный файл

## Статические свойства
| Свойство | Тип | Назначение |
|----------|-----|-----------|
| `TelegramServiceInstance` | `TelegramService` | Доступ из других сервисов для уведомлений |
| `Settings` | `AppSettings` | Загружается при объявлении поля через `AppSettings.Load()` |
| `TwitchChecker` | `TwitchCheckerService` | Фоновый мониторинг |
| `TwitchDownloader` | `TwitchDownloaderService` | Запуск загрузок |

## Ключевые методы
| Метод | Описание |
|-------|---------|
| `Main(args: string[]): Task` | UTF-8 → settings → создание зависимостей → recovery → Telegram/checker → `ExitAsync`. |
| `ExitAsync(): Task` | Ждёт `STOP`/сигнал, затем за 10-секундный budget отменяет recovery/checker/downloader и сохраняет настройки. |
| `RecoverInterruptedDownloadsAsync(cancellationToken: CancellationToken): Task` | В фоне завершает orphan `.recording` и логирует результат каждого файла. |
| `SettingsChecker(): void` | Проверяет env/file-настройки и интерактивно запрашивает недостающие Telegram-поля. |
| `ConsoleWriteLine(message: string, color: ConsoleColor): void` | Приватный логгер с префиксом `[CLI]`. |
| `Uptime: string` | Возвращает время работы в формате `HH:MM`. |

## Зависимости
- Использует: [[modules/v2-app-settings]], [[modules/v2-telegram-service]], [[modules/v2-twitch-checker]], [[modules/v2-twitch-downloader]]
- Используется в: все сервисы обращаются к `Program.Settings`, `Program.TelegramServiceInstance` и т.д.

## Важные детали
- `Settings` инициализируется при объявлении поля — то есть до начала `Main`. Если файл повреждён, `Load()` пишет ошибку и возвращает дефолт.
- Headless-запуск без обязательных Telegram-настроек завершается с понятной ошибкой вместо ожидания stdin.
- `ExitAsync()` подписан на `Console.CancelKeyPress` и `ProcessExit`; это позволяет Docker-контейнеру завершаться через сигнал.
- `Uptime` считается от `_startTime = DateTime.Now` (момент загрузки класса).
- Версия в выводе: `Версия 2.0.0`.
- Зависимости между сервисами реализованы через **глобальное статическое состояние** (`Program.X`), не через DI.
- Twitch playback использует один shared `HttpClient`; downloader получает Telegram как `IRecordingNotificationSink`, а checker — интерфейс downloader.
- Recovery запускается до checker, а перечисление orphan-файлов выполняется до появления новых active-сессий.
- ~~`Exit()`~~ (удалён: 2026-09-10) — заменён на ожидаемый `ExitAsync()`.
