# TwitchDownloader — База знаний

> Главная точка входа в документацию проекта. Актуализировано: 2026-09-10

## О проекте

**TwitchDownloader** — консольное приложение для автоматической записи публичных Twitch-трансляций через Telegram-бота. V2 напрямую получает Twitch HLS, держит одну запись на канал, создаёт проверенный MP4 и умеет отменять сессию по UUID с защитой повторного старта до offline.

В репозитории сосуществуют **две версии**:
- `TwitchDownloader.CLI` — первая версия (.NET 8, требует прав администратора, файлы `token`/`id` для конфигурации).
- `TwitchDownloader2.CLI` — переработанная версия (.NET 10, кроссплатформенный запуск, env-конфигурация и основная активная разработка).

## Навигация

### Архитектура
- [[Архитектура]] — стек, компоненты, сквозные потоки и правила расширения
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
| [[modules/v2-tests]] | `TwitchDownloader2.CLI.Tests` | xUnit-проверки HLS, сессий, Telegram, shutdown и recovery |

### Внешние интерфейсы и развёртывание
| Файл | Компонент | Описание |
|------|-----------|----------|
| [[api/telegram-commands]] | Telegram API | Команды, кнопки и callback'и v2 |
| [[api/external-tools]] | `ffmpeg` / `ffprobe` | Media-финализация v2 и legacy-зависимости v1 |
| [[api/docker]] | Docker Compose | Образ, env-файл, volumes и запуск v2 |
| [[Deployment/updatetwitch]] | `scripts/updatetwitch` | Безопасное обновление сервера одной командой |

Исторические заметки в `changelog/` сохранены для совместимости с прежней
структурой vault; новые changelog-файлы не создаются, историю изменений ведёт git.

## Стек технологий

| Слой | Технология |
|------|-----------|
| Язык | C# (nullable enabled, implicit usings) |
| Runtime v1 | .NET 8 (`net8.0-windows10.0.26100.0`, x64) |
| Runtime v2 | .NET 10 (`net10.0`) |
| Telegram | `Telegram.Bot` 22.0.2 (v1) / 22.7.4 (v2) |
| Внешние CLI | v2: `ffmpeg`, `ffprobe`; v1: также `yt-dlp` |
| Сериализация | `System.Text.Json` + Base64 (v2) |
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
| `scripts/updatetwitch` | Проверка репозитория, обновление, сборка и восстановление контейнера |

## Правила обновления базы знаний

При изменении функциональности файла или метода обновляй заметку соответствующего
компонента на месте. Новый компонент → заметка в соответствующем домене и ссылка
здесь; изменения сквозных потоков или зависимостей → [[Архитектура]].

Формат краткого описания метода:

```text
methodName(param: Type): ReturnType — [одна строка что делает]
```

Wikilinks должны вести на существующий файл в формате `Файл` или `Домен/Файл`.
Changelog не ведём — история изменений уже есть в git.
