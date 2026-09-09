# v2: AppSettings

Parent: [[Index]]

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
| `PausedUntilOfflineChannels` | `List<string>` | `[]` | Каналы, которым после ручной остановки запрещён повторный старт до подтверждённого offline |
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
| `AppSettings(writeSettingsFile: Action<string, string>): AppSettings` | Внутренний конструктор подменяет файловую запись в конкурентных тестах. |
| `Save(): void` | Сохраняет настройки и логирует ошибку, не выбрасывая её обычным UI-вызовам. |
| `SaveOrThrow(): void` | Под единым state-lock создаёт и записывает Base64 JSON, не позволяя устаревшему параллельному снимку затереть pause; ошибку передаёт downloader. |
| `static Load(): AppSettings` | Читает и декодирует файл; при ошибке/отсутствии возвращает defaults. |
| `GetTrackedChannelsSnapshot(): IReadOnlyList<string>` | Возвращает нормализованный case-insensitive снимок без дублей. |
| `AddTrackedChannel(channel: string): bool` / `RemoveTrackedChannel(channel: string): bool` | Потокобезопасно меняют tracked-список; удаление также удаляет pause. |
| `IsPausedUntilOffline(channel: string): bool` | Проверяет persisted-запрет повторного старта. |
| `AddPausedChannel(channel: string): bool` / `RemovePausedChannel(channel: string): bool` | Потокобезопасно управляют pause-until-offline. |
| `ConsoleWriteLine(message: string, color: ConsoleColor): void` | Приватный логгер с префиксом `[AppSettings]`. |

## Пути (приватные статические)
- `DataDir = {BaseDirectory}/Data`
- `FilePath = {DataDir}/settings.data`

## Зависимости
- Используется в: [[modules/v2-program]] (глобальная `Program.Settings`), [[modules/v2-telegram-service]], [[modules/v2-twitch-checker]], [[modules/v2-twitch-downloader]]

## Важные детали
- **Base64 поверх JSON** — не шифрование, лишь обфускация. Если токен и ID переданы через env, они не сохраняются в `settings.data`.
- Env имеет приоритет над одноимёнными полями из файла. Это позволяет передавать секреты в Docker через `.env`/secret-хранилище.
- При отсутствии обязательных значений интерактивный локальный запуск запрашивает их в консоли; headless-запуск завершается с ошибкой и просит задать env.
- Поля с `[JsonIgnore]` (`_serviceName`, `_consoleColor`, `_stateLock`, `_writeSettingsFile`, `DataDir`, `FilePath`) не попадают в сериализацию.
- `Save()` вызывается:
  - В `Program.ExitAsync()` при завершении.
  - В `Program.SettingsChecker()` после интерактивного ввода.
  - В `TelegramService` после изменений через бота (добавление/удаление канала, смена пути).
- Канал в `TrackedChannels` нормализуется при добавлении: `lowercase` + `ExtractChannelName()` (см. [[modules/v2-telegram-service]]).
- Обе коллекции нормализуются после загрузки; pause сохраняется в том же Base64 JSON и переживает рестарт.
- Мутации коллекций и весь `SaveOrThrow()` сериализованы одним lock: параллельные stop/offline
  не могут одновременно писать `settings.data` или опубликовать более старый снимок после нового.
- Если `SaveOrThrow()` не может записать pause, downloader откатывает изменение и продолжает запись вместо ложного `Accepted`.
