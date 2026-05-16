# v1: TwitchDownloader.CLI (legacy)

Parent: [[index]]

## Назначение
Первая версия проекта. Требует прав администратора, использует inline-кнопки в Telegram (а не reply-клавиатуры как в v2), хранит каналы в plain-text файле. Сохранена в репозитории как legacy.

## Файлы
| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка входа, проверка admin, чтение `token` и `id` файлов, бесконечное `Thread.Sleep` |
| `DownloadService.cs` | Параллельная загрузка видео + 2 аудиопотоков через ffmpeg в отдельных окнах. `ConcurrentDictionary` для активных процессов и каналов |
| `ConverterService.cs` | Конвертация: audio → mp3, video → silent mp4, затем merge в `_final.mp4`. Создаёт временную папку `temp` |
| `TelegramService.cs` | Inline-клавиатуры главного меню, `MonitorChannels` поток с `yt-dlp -g` каждые 60 сек, обработка callback'ов вида `remove:<channel>`, `download_live`/`download_archive` |

## Ключевые отличия от v2
| Аспект | v1 | v2 |
|--------|----|----|
| Runtime | .NET 8 | .NET 10 |
| Хранение настроек | `token`, `id`, `tracked_channels.txt` отдельно | Единый `Data/settings.data` (Base64 JSON) |
| Клавиатуры | Inline (`InlineKeyboardMarkup`) | Reply (`ReplyKeyboardMarkup`) |
| Состояние диалога | `Dictionary<long, string> _pendingActions` | Булевы триггеры `_addChannelTrigger` и т.д. |
| Имена файлов | `{channel}_{type}_{guidShort6}.{ext}` (`audio1`/`audio2`/`video`) | `{channel}_{type}_{N}_{sessionCode6}.{ext}` |
| Дедупликация | Нет | SHA256-сравнение, удаление дубликата |
| Расширения | video=`.ts` (комментарий упоминает `.mp4`), audio=`.aac` (`-acodec copy`) | video=`.ts` mpegts, audio=`.aac` adts перекодированное |
| Уведомления | `NotifyDownloadStart`/`NotifyDownloadComplete` методы | Inline в `StartDownload` |
| Конвертация | Есть `ConverterService` (но callback `convert:` закомментирован) | Нет |
| Права | Требует Administrator | Не требует |

## Важные детали
- `Telegram.Bot` v22.0.2 — старый API: `SendTextMessageAsync`, `GetMeAsync` (в v2 — `SendMessage`, `GetMe`).
- `MonitorChannels` спит ровно 60_000 ms даже после ошибки.
- В `ConverterService.ConvertAndMergeAsync("", "")` вызывается с пустыми строками из callback `convert:` — функционал нерабочий, заготовка.
- Имя канала из ссылки извлекается как `Path.GetFileNameWithoutExtension(message.Text)` — для `https://twitch.tv/foo` даст `foo`, но для урлов с параметрами — нет.
