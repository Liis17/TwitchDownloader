# TwitchDownloader — Project Index

> Главная точка входа в документацию проекта. Создано: 2026-05-16

## О проекте

**TwitchDownloader** — консольное приложение под Windows для автоматического скачивания трансляций с Twitch через Telegram-бота. Управление осуществляется полностью через Telegram: добавление/удаление отслеживаемых каналов, ручная загрузка по ссылке, мониторинг статусов. Загрузка ведётся параллельно несколькими потоками (видео + аудио) через `ffmpeg`, ссылки на HLS получаются через `yt-dlp`.

В репозитории сосуществуют **две версии**:
- `TwitchDownloader.CLI` — первая версия (.NET 8, требует прав администратора, файлы `token`/`id` для конфигурации).
- `TwitchDownloader2.CLI` — переработанная версия (.NET 10, AppSettings с base64-сериализацией, более чистая архитектура сервисов, основная активная разработка).

## Быстрая навигация

### 🏗 Архитектура
- [[structure/overview]] — Общая структура репозитория и решения
- [[structure/entrypoints]] — Точки входа и порядок запуска сервисов

### 📦 Модули (актуальная версия v2)
- [[modules/v2-program]] — `Program.cs`, главный класс и оркестрация сервисов
- [[modules/v2-app-settings]] — `AppSettings`, хранение и сериализация настроек
- [[modules/v2-telegram-service]] — Telegram-бот, обработка апдейтов, состояние диалога
- [[modules/v2-twitch-checker]] — Фоновый мониторинг live-статуса каналов
- [[modules/v2-twitch-downloader]] — Запуск ffmpeg-сессий и хеш-дедупликация
- [[modules/v2-keyboards]] — Описания клавиатур Telegram-бота

### 📦 Модули (легаси v1)
- [[modules/v1-overview]] — Обзор первой версии `TwitchDownloader.CLI`

### 🔧 API & Методы
- [[api/telegram-commands]] — Команды и кнопки Telegram-бота
- [[api/external-tools]] — Внешние зависимости: `yt-dlp`, `ffmpeg`

### 📋 Изменения
- [[changelog/2026-05-16]] — Инициализация vault

## Стек технологий

| Слой | Технология |
|------|-----------|
| Язык | C# (nullable enabled, implicit usings) |
| Runtime v1 | .NET 8 (`net8.0-windows10.0.26100.0`, x64) |
| Runtime v2 | .NET 10 (`net10.0`) |
| Telegram | `Telegram.Bot` 22.0.2 (v1) / 22.7.4 (v2) |
| Внешние CLI | `ffmpeg`, `yt-dlp` (должны быть в PATH) |
| Сериализация | `System.Text.Json` + Base64 (v2) |
| Платформа | Windows 10/11 (v1 требует прав администратора) |

## Ключевые файлы

| Файл | Назначение |
|------|-----------|
| `TwitchDownloader.sln` | Solution-файл, объединяет обе версии |
| `TwitchDownloader2.CLI/Program.cs` | Точка входа v2 |
| `TwitchDownloader2.CLI/AppSettings.cs` | Настройки v2 (Base64 JSON) |
| `TwitchDownloader.CLI/Program.cs` | Точка входа v1, проверка прав администратора |
| `README.md` | Описание возможностей и инструкции запуска |
