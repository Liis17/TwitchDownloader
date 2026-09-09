# v2: Program

Parent: [[index]]

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
| `Main(string[] args)` | UTF-8 → SettingsChecker → запуск Telegram → Downloader → Checker → Exit |
| `Exit()` | В интерактивном режиме ждёт `STOP`/`Ctrl+C`, а при redirected stdin ждёт сигнал завершения; сохраняет настройки и останавливает сервисы |
| `SettingsChecker()` | Использует env/file-настройки; интерактивно запрашивает токен бота и ID администратора только если они отсутствуют |
| `ConsoleWriteLine(message, color)` | Приватный логгер с префиксом `[CLI]` зелёного цвета |
| `Uptime` (getter) | Возвращает время работы в формате `HH:MM` |

## Зависимости
- Использует: [[modules/v2-app-settings]], [[modules/v2-telegram-service]], [[modules/v2-twitch-checker]], [[modules/v2-twitch-downloader]]
- Используется в: все сервисы обращаются к `Program.Settings`, `Program.TelegramServiceInstance` и т.д.

## Важные детали
- `Settings` инициализируется при объявлении поля — то есть до начала `Main`. Если файл повреждён, `Load()` пишет ошибку и возвращает дефолт.
- Headless-запуск без обязательных Telegram-настроек завершается с понятной ошибкой вместо ожидания stdin.
- `Exit()` подписан на `Console.CancelKeyPress` и `ProcessExit`; это позволяет Docker-контейнеру завершаться через сигнал.
- `Uptime` считается от `_startTime = DateTime.Now` (момент загрузки класса).
- Версия в выводе: `Версия 2.0.0`.
- Зависимости между сервисами реализованы через **глобальное статическое состояние** (`Program.X`), не через DI.
