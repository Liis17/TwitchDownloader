# Внешние зависимости

Parent: [[index]]

Приложение запускает два внешних CLI-инструмента через `System.Diagnostics.Process`. Оба обязаны быть в `PATH`.

## yt-dlp

Используется для:
- **Проверки live-статуса** канала (v2): [[modules/v2-twitch-checker]]
  ```
  yt-dlp --quiet --no-warnings --print title "https://www.twitch.tv/{channel}"
  ```
  `ExitCode == 0` ⇒ канал live. Таймаут — 30 сек.

- **Получения HLS m3u8 URL** (v2): [[modules/v2-twitch-downloader]]
  ```
  yt-dlp --no-warnings --get-url https://www.twitch.tv/{channel}
  ```
  Первая строка stdout — m3u8 URL.

- **То же в v1**: [[modules/v1-overview]]
  ```
  yt-dlp -g "{videoUrl}"
  ```
  Таймаут — 15 сек.

## ffmpeg

Используется для скачивания HLS потоков и (только v1) конвертации.

### v2 — параллельные потоки в отдельных окнах
Запуск через `cmd.exe /c start "{title}" /WAIT ffmpeg <args>` — окно остаётся видимым.

Общие флаги (для реконнектов на нестабильной сети):
```
-hide_banner -loglevel warning -y
-reconnect 1 -reconnect_streamed 1 -reconnect_at_eof 1 -reconnect_on_network_error 1
-reconnect_delay_max 10
```

Видео: `-c copy -f mpegts` в `.ts`
Аудио: `-vn -c:a aac -b:a 160k -f adts` в `.aac`

### v1 — `DownloadService.StartFfmpegProcess`
Video: `-i {url} -c copy {file}.ts`
Audio: `-i {url} -vn -acodec copy {file}.aac` (без перекодирования)
Audio2: то же, с задержкой `Task.Delay(1000)` перед стартом.

### v1 — `ConverterService.ConvertAndMergeAsync`
Цепочка из трёх вызовов ffmpeg:
1. `-i audio.aac -codec:a libmp3lame -qscale:a 2 audio.mp3`
2. `-i video.ts -c:v copy -an silent_video.mp4`
3. `-i silent.mp4 -i audio.mp3 -c:v copy -c:a aac -map 0:v:0 -map 1:a:0 output_final.mp4`

## Требования к среде (из README)
- Windows 10/11 x64
- .NET 8 Runtime (v1) / .NET 10 Runtime (v2)
- Права администратора для v1
- `ffmpeg`, `yt-dlp` в PATH
