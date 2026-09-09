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
| `RecordAsync(channel: string, initialPlaylistUri: Uri, rawPath: string, cancellationToken: CancellationToken): Task<LiveRecordingResult>` | Дедуплицирует sequence, считает дыры/рекламу и пишет MPEG-TS либо fMP4. |
| `FinalizeAsync(rawPath: string, expectedDurationSeconds: double, container: RecordingContainer, cancellationToken: CancellationToken): Task<MediaFinalizeResult>` | Создаёт `.mp4.part`, синхронизирует аудио, проверяет и атомарно публикует `.mp4`. |
| `RecoverOrphansAsync(directory: string, cancellationToken: CancellationToken): Task<IReadOnlyList<MediaFinalizeResult>>` | Определяет контейнер и duration каждого `*.recording`, затем финализирует. |
| `RunAsync(executable: string, arguments: IReadOnlyList<string>, cancellationToken: CancellationToken): Task<MediaToolResult>` | Запускает media tool без shell и уничтожает дерево процесса при отмене. |

## Инварианты сохранности

- Init-сегмент из `EXT-X-MAP` записывается перед соответствующими CMAF-сегментами и повторяется
  только при смене URI.
- После четырёх неудачных попыток сегмент считается дырой; неизвестная рекламная разметка
  работает fail-open и контент сохраняется.
- Валидный `.mp4.part` переименовывается в `.mp4` только после `ffprobe`.
- При ошибке сырьё становится `.recording.failed`, а кандидат — `.mp4.failed`; если media-кандидата нет, последний содержит диагностику.
- Старые файлы `*_video_*.ts` и `*_audio_*.aac` восстановитель не перечисляет.
- При process shutdown токены recorder/finalizer отменяются: `.recording` остаётся для следующего recovery, а незавершённый `.mp4.part` будет заменён новой попыткой.

## Зависимости

- Использует: [[modules/v2-twitch-playback]], `ffmpeg`, `ffprobe`.
- Используется в: [[modules/v2-twitch-downloader]].
