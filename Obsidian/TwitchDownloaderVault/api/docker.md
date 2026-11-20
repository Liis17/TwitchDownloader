# Docker: TwitchDownloader2

Parent: [[Index]]

## Файлы
- `docker/Dockerfile` — multi-stage образ на базе .NET 10; устанавливает пакет `ffmpeg` (`ffmpeg` + `ffprobe`), без `yt-dlp`.
- `docker/docker-compose.yml` — сервис с env-файлом, volume для настроек и volume для загрузок.
- `docker/.env.example` — шаблон переменных окружения.
- `docker/README.md` — команды подготовки, запуска, просмотра логов и остановки.
- `update.sh` — обновление ветки, сборка образа и перезапуск Compose-сервиса.
- `.dockerignore` — исключает из build context `.git`, Obsidian, артефакты сборки, секретный `.env` и runtime-каталоги.

## Переменные окружения
- `TELEGRAM_BOT_TOKEN` — токен бота.
- `TELEGRAM_OWNER_ID` — числовой ID владельца.

`DOWNLOAD_PATH` поддерживается приложением как необязательная env-переменная для
обычного запуска, но предоставленный Compose-пример задаёт `/app/Downloads`
явно и монтирует туда `docker/downloads/`; это значение не нужно добавлять в
`docker/.env` для данного Compose-примера.

Токен и ID имеют приоритет над `Data/settings.data` и не записываются в этот файл при сохранении настроек.

## Volumes
- `docker/data/` → `/app/Data` — отслеживаемые каналы, прочие сохраняемые настройки и история записей `history.db`.
- `docker/downloads/` → `/app/Downloads` — скачанные файлы.

Контейнер запускается без TTY и завершает приложение по `SIGINT`; v2 ожидает сигнал завершения вместо чтения из stdin. `stop_grace_period: 30s` сохраняется: приложение использует внутренний budget 10 секунд, закрывает active `.recording` без финализации и восстанавливает их при следующем старте.

## Автообновление

На сервере из корня репозитория запускается `./update.sh`: ветка `master`
подтягивается из `origin`, образ собирается при работающем контейнере, затем Compose
пересоздаёт контейнер. При ошибке сборки прежний контейнер продолжает работать.
`docker/data/`, `docker/downloads/` и `docker/.env` не затрагиваются. См. [[Deployment/update]].
