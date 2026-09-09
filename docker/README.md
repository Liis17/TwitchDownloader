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

Скрипт `scripts/updatetwitch` обновляет ветку `master`, останавливает контейнер,
пересобирает образ и запускает его снова. Если сборка или запуск завершатся
ошибкой, скрипт попытается запустить прежний контейнер.

Один раз на сервере создай команду, доступную из любого каталога:

```bash
chmod +x /opt/TwitchDownloader/scripts/updatetwitch
ln -s /opt/TwitchDownloader/scripts/updatetwitch /usr/local/bin/updatetwitch
```

После этого обновление выполняется одной командой:

```bash
updatetwitch
```

Скрипт не перезаписывает `docker/.env` и не удаляет volumes с настройками и
скачанными файлами. Проект фиксирован в `/opt/TwitchDownloader`, ветка —
`master`.

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
