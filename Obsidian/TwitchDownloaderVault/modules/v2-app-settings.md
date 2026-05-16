# v2: AppSettings

Parent: [[index]]

## Назначение
Класс настроек приложения v2. Хранит конфигурацию в Base64-encoded JSON в файле `Data/settings.data` рядом с exe. Загрузка и сохранение через статические методы.

## Файлы
- `TwitchDownloader2.CLI/AppSettings.cs`

## Поля настроек
| Поле | Тип | Дефолт | Назначение |
|------|-----|--------|-----------|
| `TelegramToken` | `string` | `""` | Токен Telegram-бота |
| `TelegramIdOwner` | `long` | `0` | ID администратора (единственный разрешённый user) |
| `TrackedChannels` | `List<string>` | `[]` | Имена отслеживаемых Twitch-каналов (lowercase, без url-префикса) |
| `DownloadPath` | `string` | `{BaseDirectory}/Downloads` | Папка сохранения файлов |

## Ключевые методы
| Метод | Описание |
|-------|---------|
| `Save()` | Сериализует this → JSON → Base64 → пишет в `Data/settings.data`. Создаёт `Data/` если нет |
| `static Load()` | Читает файл, декодирует Base64, десериализует JSON. При ошибке/отсутствии — возвращает `new AppSettings()` |
| `ConsoleWriteLine(...)` | Приватный логгер с префиксом `[AppSettings]` |

## Пути (приватные статические)
- `DataDir = {BaseDirectory}/Data`
- `FilePath = {DataDir}/settings.data`

## Зависимости
- Используется в: [[modules/v2-program]] (глобальная `Program.Settings`), [[modules/v2-telegram-service]], [[modules/v2-twitch-checker]], [[modules/v2-twitch-downloader]]

## Важные детали
- **Base64 поверх JSON** — не шифрование, лишь обфускация. Токен лежит в файле в декодируемом виде.
- Поля с `[JsonIgnore]` (`_serviceName`, `_consoleColor`, `DataDir`, `FilePath`) не попадают в сериализацию.
- `Save()` вызывается:
  - В `Program.Exit()` при завершении.
  - В `Program.SettingsChecker()` после интерактивного ввода.
  - В `TelegramService` после изменений через бота (добавление/удаление канала, смена пути).
- Канал в `TrackedChannels` нормализуется при добавлении: `lowercase` + `ExtractChannelName()` (см. [[modules/v2-telegram-service]]).
