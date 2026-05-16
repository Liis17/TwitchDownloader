# Структура проекта

Parent: [[index]]

## Дерево директорий

```
TwitchDownloader/
├── TwitchDownloader.sln           # Solution с двумя проектами
├── README.md                      # Описание и инструкции
├── .gitignore / .gitattributes
│
├── TwitchDownloader.CLI/          # Версия 1 (.NET 8, legacy)
│   ├── Program.cs                 # Точка входа, проверка admin прав
│   ├── DownloadService.cs         # Управление ffmpeg-процессами
│   ├── ConverterService.cs        # Конвертация/слияние видео+аудио
│   ├── TelegramService.cs         # Бот: меню, отслеживание, мониторинг
│   ├── DB/                        # (пустая папка)
│   ├── Properties/
│   │   └── PublishProfiles/
│   ├── TwitchDownloader.CLI.csproj
│   └── bin/ obj/
│
├── TwitchDownloader2.CLI/         # Версия 2 (.NET 10, актуальная)
│   ├── Program.cs                 # Оркестрация сервисов
│   ├── AppSettings.cs             # Настройки (Base64-encoded JSON)
│   ├── TelegramService.cs         # Бот с триггерами состояний
│   ├── TwitchCheckerService.cs    # Фоновая проверка live-статуса
│   ├── TwitchDownloaderService.cs # Запуск ffmpeg, хеш-дедупликация
│   ├── Keyboards.cs               # Reply/Inline клавиатуры бота
│   ├── Properties/
│   │   ├── PublishProfiles/
│   │   └── launchSettings.json
│   ├── TwitchDownloader2.CLI.csproj
│   └── bin/ obj/
│
└── Obsidian/
    └── TwitchDownloaderVault/     # Эта vault-память проекта
```

## Описание директорий

### `TwitchDownloader.CLI/` — версия 1 (legacy)
.NET 8 проект, требует прав администратора. Использует файлы `token` и `id` рядом с exe для хранения конфигурации. Каналы хранятся в `tracked_channels.txt`. См. [[modules/v1-overview]].

### `TwitchDownloader2.CLI/` — версия 2 (актуальная)
.NET 10 проект. Конфигурация хранится в `Data/settings.data` (Base64-encoded JSON). Управление через Reply-клавиатуры Telegram. Архитектура разделена на статически связанные через `Program` сервисы. См. [[modules/v2-program]].

### `Obsidian/TwitchDownloaderVault/`
Долговременная память проекта в формате Obsidian Vault. Связи между заметками через wikilinks.

## Конфигурационные файлы

| Файл | Назначение |
|------|-----------|
| `TwitchDownloader.sln` | VS solution, format 12.00, VS18 |
| `*.csproj` | Project files (см. [[structure/entrypoints]]) |
| `Properties/launchSettings.json` | Профиль запуска для v2 |
| `Properties/PublishProfiles/` | Профили публикации (пустые в обоих проектах) |
| `Data/settings.data` (runtime, v2) | Base64 JSON с настройками |
| `token`, `id` (runtime, v1) | Токен бота и Telegram ID администратора |
| `tracked_channels.txt` (runtime, v1) | Список отслеживаемых каналов |
