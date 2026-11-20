# Запуск TwitchDownloader2 в Docker

## 1. Подготовить переменные окружения

```bash
cp docker/.env.example docker/.env
```

Открой `docker/.env` и укажи:

- `TELEGRAM_BOT_TOKEN` — токен от `@BotFather`;
- `TELEGRAM_OWNER_ID` — числовой Telegram ID владельца.

В приложении также поддерживается `DOWNLOAD_PATH`, но предоставленный Compose-пример
намеренно фиксирует его на `/app/Downloads`, чтобы volume всегда совпадал с путём
загрузки. Для изменения папки на Mac отредактируй левую часть volume ниже.

## 2. Запустить

Из корня репозитория выполни:

```bash
docker compose -f docker/docker-compose.yml up -d --build
```

Логи:

```bash
docker compose -f docker/docker-compose.yml logs -f
```

Остановить контейнер:

```bash
docker compose -f docker/docker-compose.yml down
```

## Автообновление на сервере

Из корня репозитория на сервере выполни:

```bash
./update.sh
```

Скрипт подтягивает ветку `master`, собирает новый образ, пока работает прежний
контейнер, и затем пересоздаёт контейнер. Если сборка завершится ошибкой, прежний
контейнер продолжит работать.

Скрипт не перезаписывает `docker/.env` и не удаляет `docker/data/` (настройки и
история записей) и `docker/downloads/` (записи).

Настройки и скачанные файлы сохраняются в `docker/data/` и `docker/downloads/`.
Токен и ID берутся из env и не записываются в `Data/settings.data`.

В этом Compose-примере путь внутри контейнера фиксирован как `/app/Downloads`.
Чтобы изменить host-папку, отредактируй левую часть volume в
`docker/docker-compose.yml`; путь внутри контейнера менять не нужно.

Docker Desktop на macOS сам выберет подходящую архитектуру образа, поэтому
дополнительный `platform` для Apple Silicon не требуется.

## Как контейнер завершает запись

- В runtime-образе нужны только `ffmpeg` и входящий в тот же пакет `ffprobe`; `yt-dlp`
  для v2 больше не устанавливается.
- Compose отправляет `SIGINT` и сохраняет `stop_grace_period: 30s`.
- Приложение прекращает новые проверки, отменяет HTTP-запросы и media tools и закрывает
  текущие `.recording` без долгой финализации.
- На следующем старте orphan `.recording` обнаруживаются в `/app/Downloads` и собираются
  в MP4 фоновым recovery-проходом. Старые `.ts`/`.aac` не затрагиваются.

Ручная остановка из Telegram отличается от shutdown: она сразу запрещает повторный старт
канала до offline, но продолжает финализацию частичного MP4 в работающем контейнере.
