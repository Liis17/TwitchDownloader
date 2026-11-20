# TwitchDownloader — База знаний

> Главная точка входа в документацию проекта.

## О проекте

**TwitchDownloader** — консольное приложение для автоматической записи публичных Twitch-трансляций через Telegram-бота. V2 напрямую получает Twitch HLS, держит одну recording-сессию на канал, освобождает канал на время финализации для возможного переподключения стримера, создаёт проверенный MP4 и умеет отменять сессию по UUID с защитой повторного старта до offline.

В репозитории сосуществуют **две версии**:
- `TwitchDownloader.CLI` — первая версия (.NET 8, требует прав администратора, файлы `token`/`id` для конфигурации).
- `TwitchDownloader2.CLI` — переработанная версия (.NET 10, кроссплатформенный запуск, env-конфигурация и основная активная разработка).

## Навигация

### Архитектура
- [[Architecture|Архитектура]] — стек, компоненты, сквозные потоки и ограничения
- [[Glossary|Словарь терминов]] — понятия предметной области и их основные названия
- [[structure/overview]] — подробный обзор дерева репозитория
- [[structure/entrypoints]] — точки входа и порядок запуска сервисов

### TwitchDownloader.CLI (legacy)
| Файл | Компонент | Описание |
|------|-----------|----------|
| [[modules/v1-overview]] | `TwitchDownloader.CLI` | Legacy-бот и загрузчик на .NET 8 |

### TwitchDownloader2.CLI (основная версия)
| Файл | Компонент | Описание |
|------|-----------|----------|
| [[modules/v2-program]] | `Program` | Инициализация сервисов и завершение приложения |
| [[modules/v2-app-settings]] | `AppSettings` | Base64 JSON, env-переопределения и сохранение настроек |
| [[modules/v2-telegram-service]] | `TelegramService` | Telegram API, команды владельца и состояния ввода |
| [[modules/v2-twitch-checker]] | `TwitchCheckerService` | Периодическая проверка live-статуса каналов |
| [[modules/v2-twitch-downloader]] | `TwitchDownloaderService` | HLS/ffmpeg-сессии, дедупликация и ручная остановка |
| [[modules/v2-twitch-playback]] | `TwitchPlaybackClient` | Прямой Twitch GraphQL/Usher и разбор HLS playlist |
| [[modules/v2-media-recording]] | `LiveStreamRecorder` / `MediaFinalizer` | Запись HLS-сегментов, проверка MP4 и восстановление сырья |
| [[modules/v2-keyboards]] | `Keyboards` | Reply/inline-клавиатуры бота |
| [[modules/v2-recording-history]] | `RecordingHistoryStore` | История записей в SQLite (`Data/history.db`) |
| [[modules/v2-tests]] | `TwitchDownloader2.CLI.Tests` | xUnit-проверки HLS, сессий, Telegram, shutdown и recovery |

### Внешние интерфейсы и развёртывание
| Файл | Компонент | Описание |
|------|-----------|----------|
| [[api/telegram-commands]] | Telegram API | Команды, кнопки и callback'и v2 |
| [[api/external-tools]] | `ffmpeg` / `ffprobe` | Media-финализация v2 и legacy-зависимости v1 |
| [[api/docker]] | Docker Compose | Образ, env-файл, volumes и запуск v2 |
| [[Deployment/update]] | `update.sh` | Обновление сервера одной командой без потери данных контейнера |

## Стек технологий

| Слой | Технология |
|------|-----------|
| Язык | C# (nullable enabled, implicit usings) |
| Runtime v1 | .NET 8 (`net8.0-windows10.0.26100.0`, x64) |
| Runtime v2 | .NET 10 (`net10.0`) |
| Telegram | `Telegram.Bot` 22.0.2 (v1) / 22.7.4 (v2) |
| Внешние CLI | v2: `ffmpeg`, `ffprobe`; v1: также `yt-dlp` |
| Сериализация | `System.Text.Json` + Base64 (v2) |
| История записей | SQLite через `Microsoft.Data.Sqlite` (v2) |
| Платформа | v1: Windows 10/11; v2: Windows, macOS, Linux и Docker |
| Развёртывание | Docker multi-stage + Compose; updater на Bash |

## Ключевые файлы

| Файл | Назначение |
|------|-----------|
| `TwitchDownloader.sln` | Solution-файл, объединяет обе версии и тестовый проект v2 |
| `TwitchDownloader2.CLI/Program.cs` | Точка входа v2 |
| `TwitchDownloader2.CLI/AppSettings.cs` | Настройки v2 (Base64 JSON) |
| `TwitchDownloader.CLI/Program.cs` | Точка входа v1, проверка прав администратора |
| `README.md` | Описание возможностей и инструкции запуска |
| `docker/Dockerfile` | Сборка и runtime-образ v2 с `ffmpeg`/`ffprobe` |
| `docker/docker-compose.yml` | Сервис v2 и volumes для настроек/загрузок |
| `update.sh` | Проверка репозитория, `git pull`, сборка образа и перезапуск контейнера |

## Обновление памяти

Заметки отражают текущее состояние проекта. Обновляй затронутые контракты, потоки,
зависимости, ограничения и основания решений; удаляй неверные сведения.
Имена файлов и папок доменов — на английском. Changelog и дневники задач не ведутся.
