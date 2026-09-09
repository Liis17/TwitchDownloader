# TwitchDownloader

Сервис автоматически записывает публичные Twitch-трансляции отслеживаемых каналов и
управляется через Telegram-бота. Основная версия — `TwitchDownloader2.CLI` на .NET 10;
legacy-проект `TwitchDownloader.CLI` остаётся без изменений.

## Что умеет v2

- Проверяет несколько каналов параллельно, но держит не больше одной сессии на канал.
- Получает playback token и HLS напрямую через Twitch GraphQL/Usher без `yt-dlp`.
- Выбирает source-качество, а при его отсутствии явно предупреждает о fallback.
- Пишет один поток MPEG-TS или fMP4/CMAF, дедуплицирует HLS-сегменты и считает дыры.
- Пропускает рекламу только по безопасному признаку `amazon`/`stitched` в заголовке `EXTINF`.
- Создаёт `.mp4.part`, синхронизирует звук и проверяет длительность/A-V/DTS через `ffprobe`
  перед публикацией итогового `.mp4`.
- Позволяет остановить запись из главного меню Telegram. Частичная запись всё равно
  финализируется; повторный старт этого канала запрещён до подтверждённого offline.
- При остановке контейнера быстро закрывает `.recording` без финализации, а при следующем
  запуске автоматически восстанавливает такие файлы в фоне.

Поддерживаются публично доступные live-трансляции без cookies и subscriber-only авторизации.

## Запуск в Docker

```bash
cp docker/.env.example docker/.env
# заполнить TELEGRAM_BOT_TOKEN и TELEGRAM_OWNER_ID
docker compose -f docker/docker-compose.yml up -d --build
docker compose -f docker/docker-compose.yml logs -f
```

Настройки хранятся в `docker/data/`, записи — в `docker/downloads/`. Compose оставляет
контейнеру 30 секунд на shutdown. Подробности и серверное обновление:
[docker/README.md](docker/README.md).

## Локальный запуск v2

Требуются .NET 10 SDK/runtime и доступные в `PATH` `ffmpeg` вместе с `ffprobe`.

```bash
export TELEGRAM_BOT_TOKEN='...'
export TELEGRAM_OWNER_ID='123456789'
export DOWNLOAD_PATH="$PWD/Downloads" # необязательно
dotnet run --project TwitchDownloader2.CLI/TwitchDownloader2.CLI.csproj
```

Для интерактивного запуска токен и ID можно ввести в консоли. В headless/Docker режиме
они обязательны в environment.

## Проверка

```bash
dotnet test TwitchDownloader.sln
dotnet build TwitchDownloader2.CLI/TwitchDownloader2.CLI.csproj --configuration Release
docker build -f docker/Dockerfile .
```

Тесты покрывают HLS parser/client, рекламу и sequence, fMP4 init-сегменты, retry/token
refresh, конкурентные сессии, Telegram callback, pause-until-offline, финализацию и recovery.

## Форматы файлов v2

Активное сырьё:

```text
live_<channel>_<yyyyMMdd-HHmmss>_source_<sessionId>.recording
```

После успешной проверки остаётся одноимённый `.mp4`. При ошибке сохраняются
`.recording.failed` и `.mp4.failed`. Если ffmpeg не успел создать media-кандидат,
`.mp4.failed` содержит диагностическое сообщение. Старые `*_video_*.ts` и
`*_audio_*.aac` автоматическое восстановление не трогает.

## Legacy v1

`TwitchDownloader.CLI` — прежняя Windows/.NET 8 версия с отдельными видео- и
аудиопроцессами. Ей по-прежнему нужны `yt-dlp` и `ffmpeg`; новая серверная схема касается
только `TwitchDownloader2.CLI`.
