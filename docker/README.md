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

Настройки и скачанные файлы сохраняются в `docker/data/` и `docker/downloads/`.
Токен и ID берутся из env и не записываются в `Data/settings.data`.

В этом Compose-примере путь внутри контейнера фиксирован как `/app/Downloads`.
Чтобы изменить host-папку, отредактируй левую часть volume в
`docker/docker-compose.yml`; путь внутри контейнера менять не нужно.

Docker Desktop на macOS сам выберет подходящую архитектуру образа, поэтому
дополнительный `platform` для Apple Silicon не требуется.
