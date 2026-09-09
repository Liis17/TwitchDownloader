# v2: AppSettings

Parent: [[index]]

## Назначение
Класс настроек приложения v2. Загружает локальную конфигурацию из Base64-encoded JSON в файле `Data/settings.data` рядом с приложением, затем применяет переменные окружения. Значения env имеют приоритет.

## Файлы
- `TwitchDownloader2.CLI/AppSettings.cs`

## Поля настроек
| Поле | Тип | Дефолт | Назначение |
|------|-----|--------|-----------|
| `TelegramToken` | `string` | `""` | Токен Telegram-бота |
| `TelegramIdOwner` | `long` | `0` | ID администратора (единственный разрешённый user) |
| `TrackedChannels` | `List<string>` | `[]` | Имена отслеживаемых Twitch-каналов (lowercase, без url-префикса) |
| `DownloadPath` | `string` | `{BaseDirectory}/Downloads` | Папка сохранения файлов |

## Переменные окружения
| Переменная | Назначение |
|------------|-----------|
| `TELEGRAM_BOT_TOKEN` | Токен Telegram-бота |
| `TELEGRAM_OWNER_ID` | Числовой ID администратора |
| `DOWNLOAD_PATH` | Необязательный путь сохранения |

## Ключевые методы
| Метод | Описание |
|-------|---------|
| `Save()` | Создаёт копию настроек (с очищенными env-секретами), сериализует её → JSON → Base64 → пишет в `Data/settings.data`. Создаёт `Data/` если нет |
| `static Load()` | Читает файл, декодирует Base64, десериализует JSON. При ошибке/отсутствии — возвращает `new AppSettings()` |
| `ConsoleWriteLine(...)` | Приватный логгер с префиксом `[AppSettings]` |

## Пути (приватные статические)
- `DataDir = {BaseDirectory}/Data`
- `FilePath = {DataDir}/settings.data`

## Зависимости
- Используется в: [[modules/v2-program]] (глобальная `Program.Settings`), [[modules/v2-telegram-service]], [[modules/v2-twitch-checker]], [[modules/v2-twitch-downloader]]

## Важные детали
- **Base64 поверх JSON** — не шифрование, лишь обфускация. Если токен и ID переданы через env, они не сохраняются в `settings.data`.
- Env имеет приоритет над одноимёнными полями из файла. Это позволяет передавать секреты в Docker через `.env`/secret-хранилище.
- При отсутствии обязательных значений интерактивный локальный запуск запрашивает их в консоли; headless-запуск завершается с ошибкой и просит задать env.
- Поля с `[JsonIgnore]` (`_serviceName`, `_consoleColor`, `DataDir`, `FilePath`) не попадают в сериализацию.
- `Save()` вызывается:
  - В `Program.Exit()` при завершении.
  - В `Program.SettingsChecker()` после интерактивного ввода.
  - В `TelegramService` после изменений через бота (добавление/удаление канала, смена пути).
- Канал в `TrackedChannels` нормализуется при добавлении: `lowercase` + `ExtractChannelName()` (см. [[modules/v2-telegram-service]]).
