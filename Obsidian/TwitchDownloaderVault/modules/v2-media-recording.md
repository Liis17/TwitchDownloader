# v2: HLS recording and MP4 finalization

Parent: [[Index]]

## Назначение

Низкоуровневый контур одной live-сессии: последовательно сохраняет уникальные HLS-сегменты
в `.recording`, учитывает пропущенные сегменты и рекламу, затем собирает и проверяет один MP4.
Тот же финализатор восстанавливает незавершённое сырьё после перезапуска процесса.

## Файлы

- `TwitchDownloader2.CLI/LiveStreamRecorder.cs` — цикл media playlist и запись сегментов.
- `TwitchDownloader2.CLI/MediaFinalizer.cs` — сборка, проверка, публикация и восстановление MP4.
- `TwitchDownloader2.CLI/MediaToolRunner.cs` — отменяемый адаптер процессов `ffmpeg`/`ffprobe`.
- `TwitchDownloader2.CLI/AsyncDelay.cs` — подменяемая задержка для retry/polling.

## Ключевые методы

| Метод | Описание |
|-------|----------|
| `RecordAsync(channel, initialPlaylistUri, rawPath, cancellationToken): Task<LiveRecordingResult>` | Дедуплицирует сегменты по media sequence, фиксирует дыры, безопасно пропускает только рекламу с `amazon`/`stitched` в `EXTINF` и пишет MPEG-TS либо fMP4. |
| `FinalizeAsync(rawPath, expectedDurationSeconds, container, cancellationToken): Task<MediaFinalizeResult>` | Перекодирует аудио с async-resample, создаёт `.mp4.part`, проверяет длительность/A-V/DTS и атомарно публикует `.mp4`. |
| `RecoverOrphansAsync(directory, cancellationToken): Task<IReadOnlyList<MediaFinalizeResult>>` | Находит только `*.recording`, определяет контейнер по сигнатуре, вычисляет длительность декодирующим проходом и запускает обычную финализацию. |
| `RunAsync(executable, arguments, cancellationToken): Task<MediaToolResult>` | Запускает внешний media tool без shell и уничтожает дерево процесса при отмене. |

## Инварианты сохранности

- Init-сегмент из `EXT-X-MAP` записывается перед соответствующими CMAF-сегментами и повторяется
  только при смене URI.
- После четырёх неудачных попыток сегмент считается дырой; неизвестная рекламная разметка
  работает fail-open и контент сохраняется.
- Валидный `.mp4.part` переименовывается в `.mp4` только после `ffprobe`.
- При ошибке сырьё становится `.recording.failed`, а кандидат — `.mp4.failed`.
- Старые файлы `*_video_*.ts` и `*_audio_*.aac` восстановитель не перечисляет.

## Зависимости

- Использует: [[modules/v2-twitch-playback]], `ffmpeg`, `ffprobe`.
- Используется в: [[modules/v2-twitch-downloader]].
