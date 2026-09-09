# Docker: TwitchDownloader2

Parent: [[index]]

## Файлы
- `docker/Dockerfile` — multi-stage образ на базе .NET 10; устанавливает `ffmpeg` и `yt-dlp`.
- `docker/docker-compose.yml` — сервис с env-файлом, volume для настроек и volume для загрузок.
- `docker/.env.example` — шаблон переменных окружения.
- `docker/README.md` — команды подготовки, запуска, просмотра логов и остановки.
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
- `docker/data/` → `/app/Data` — отслеживаемые каналы и прочие сохраняемые настройки.
- `docker/downloads/` → `/app/Downloads` — скачанные файлы.

Контейнер запускается без TTY и завершает приложение по `SIGINT`; v2 ожидает сигнал завершения вместо чтения из stdin.
