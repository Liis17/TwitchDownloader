# Внешние зависимости

Parent: [[Index]]

## V2: ffmpeg и ffprobe

`TwitchDownloader2.CLI` получает HLS через HTTP и требует два бинарника из пакета ffmpeg
в `PATH`. Процессы запускает [[modules/v2-media-recording]] через `ProcessStartInfo.ArgumentList`
без shell и с `-nostdin`; отмена уничтожает всё дерево процесса.

### Финализация

```text
ffmpeg -hide_banner -loglevel warning -nostdin -y
  -f <mpegts|mp4> -i <source.recording>
  -c:v copy -c:a aac -b:a 160k -ar 48000 -ac 2
  -af aresample=async=1:first_pts=0
  -f mp4 <output.mp4.part>
```

Входной demuxer выбирается явно: `mpegts` для TS и `mp4` для fMP4/CMAF.

### Проверка

`ffprobe` возвращает длительность video/audio stream. Финализатор сравнивает video с
суммой записанных `EXTINF` (±2 с), audio с video (±2 с) и stderr ffmpeg на
`Non-monotonous DTS`.

### Recovery duration

```text
ffmpeg -v error -stats -nostdin -f <mpegts|mp4> -i <source.recording> -f null -
```

Последний `time=...` используется как фактическая длительность orphan-записи.

## V2: удалённая зависимость yt-dlp

V2 больше не вызывает и не устанавливает `yt-dlp`: live/offline, playback token и HLS URL
получает [[modules/v2-twitch-playback]]. В Docker runtime установлен только пакет `ffmpeg`
(он включает `ffprobe`) и CA certificates.

## Legacy v1

V1 остаётся без изменений и требует `yt-dlp`/`ffmpeg`:

```text
yt-dlp -g <twitch-url>
ffmpeg -i <url> ...
```

Его `ConverterService` отдельно перекодирует аудио, создаёт silent video и объединяет их.
