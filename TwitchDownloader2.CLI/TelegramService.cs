using System.Net;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TwitchDownloader2.CLI
{
    public sealed class TelegramService : IRecordingNotificationSink
    {
        private readonly TelegramBotClient _bot;
        private readonly long _ownerId;
        private CancellationTokenSource? _cancellation;
        private Task? _startupTask;

        private const int HistoryPageSize = 10;

        private bool _addChannelTrigger;
        private bool _editDownloadPathTrigger;

        public TelegramService(string token, long ownerId)
        {
            _bot = new TelegramBotClient(token);
            _ownerId = ownerId;
        }

        public void Start()
        {
            _cancellation = new CancellationTokenSource();
            _startupTask = RunAsync(_cancellation.Token);
        }

        public void Stop()
        {
            _cancellation?.Cancel();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cancellationToken);
            var me = await _bot.GetMe(cancellationToken);
            ConsoleWriteLine($"✅ Telegram bot запущен как @{me.Username}");
        }

        private Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
        {
            ConsoleWriteLine($"Telegram Error: {exception.Message}", ConsoleColor.DarkRed);
            return Task.CompletedTask;
        }

        private async Task HandleUpdateAsync(
            ITelegramBotClient bot,
            Update update,
            CancellationToken cancellationToken)
        {
            if (update.Message is { } message)
            {
                if (message.From?.Id != _ownerId || message.Text is null)
                    return;

                var sender = string.IsNullOrWhiteSpace(message.From.Username)
                    ? message.From.Id.ToString()
                    : message.From.Username;
                ConsoleWriteLine($"{sender}: {message.Text}");
                await HandleMessageAsync(message, cancellationToken);
                return;
            }

            if (update.CallbackQuery is { } callback && callback.From.Id == _ownerId)
                await HandleCallbackAsync(bot, callback, cancellationToken);
        }

        private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
        {
            var text = message.Text!;
            if (text == "❌ Отменить действие")
            {
                DisableTriggers();
                await SendMessageAsync(
                    "❌ <b>Действие отменено</b>",
                    Keyboards.GetMainKeyboard(),
                    cancellationToken);
                return;
            }

            if (_addChannelTrigger)
            {
                var channel = ExtractChannelName(text.Replace(" ", string.Empty).ToLowerInvariant());
                DisableTriggers();
                if (!Program.Settings.AddTrackedChannel(channel))
                {
                    await SendMessageAsync(
                        $"⚠️ Канал <b>{Html(channel)}</b> уже был добавлен ранее или его имя пусто",
                        Keyboards.GetChannelsKeyboard(),
                        cancellationToken);
                    return;
                }

                Program.Settings.Save();
                Program.TwitchChecker.ForceCheck();
                await SendMessageAsync(
                    $"✨ Канал <b>{Html(channel)}</b> добавлен в отслеживаемые",
                    Keyboards.GetChannelsKeyboard(),
                    cancellationToken);
                return;
            }

            if (_editDownloadPathTrigger)
            {
                DisableTriggers();
                if (!Directory.Exists(text))
                {
                    await SendMessageAsync("❌ Такой путь не найден", Keyboards.GetMainKeyboard(), cancellationToken);
                    return;
                }

                Program.Settings.DownloadPath = text;
                Program.Settings.Save();
                await SendMessageAsync("✨ Путь изменён", Keyboards.GetPathEditKeyboard(), cancellationToken);
                await SendDownloadPathAsync(cancellationToken);
                return;
            }

            if (text.StartsWith("/start", StringComparison.Ordinal))
            {
                await SendStartMessageAsync(message, cancellationToken);
                return;
            }

            switch (text)
            {
                case "➕ Добавить":
                    DisableTriggers();
                    _addChannelTrigger = true;
                    await SendMessageAsync(
                        "Напиши имя канала или ссылку на Twitch",
                        Keyboards.GetOnlyCancelKeyboard("Вставить ссылку на Twitch сюда"),
                        cancellationToken);
                    return;

                case "🗑️ Удалить":
                    DisableTriggers();
                    var channelsToDelete = Program.Settings.GetTrackedChannelsSnapshot();
                    if (channelsToDelete.Count == 0)
                    {
                        await SendMessageAsync(
                            "ℹ️ Отслеживаемых каналов нет",
                            Keyboards.GetChannelsKeyboard(),
                            cancellationToken);
                        return;
                    }

                    await SendMessageAsync(
                        "Выбери канал, который хочешь удалить:",
                        Keyboards.GetDeleteChannelsKeyboard(channelsToDelete),
                        cancellationToken);
                    return;

                case "📺 Каналы":
                    DisableTriggers();
                    var trackedChannels = Program.Settings.GetTrackedChannelsSnapshot();
                    var channels = "---- Отслеживаемые каналы на Twitch ----\n";
                    channels += trackedChannels.Count == 0
                        ? "Пока нет ни одного канала"
                        : "<b>" + string.Join(
                            '\n',
                            trackedChannels.Select(channel =>
                                $"🎥 <a href=\"https://www.twitch.tv/{Html(channel)}\">{Html(channel)}</a>")) + "</b>";
                    await SendMessageAsync(channels, Keyboards.GetChannelsKeyboard(), cancellationToken);
                    return;

                case "🏺 История":
                    DisableTriggers();
                    await SendHistoryAsync(cancellationToken);
                    return;

                case "🏠 Главная":
                case "🏠 Вернуться на главную":
                    await SendStartMessageAsync(message, cancellationToken);
                    return;

                case "🔁 Принудительно обновить":
                    Program.TwitchChecker.ForceCheck();
                    await SendMessageAsync("<b>Проверка запрошена</b>", Keyboards.GetMainKeyboard(), cancellationToken);
                    return;

                case "📜 Статус":
                    var statuses = Program.TwitchChecker.GetStatuses();
                    var statusText = "---- Статус отслеживаемых каналов ----\n\n";
                    statusText += string.Join('\n', statuses.Select(item => $"{(item.Value ? "🔴" : "💤")} {Html(item.Key)}"));
                    await SendMessageAsync(statusText, Keyboards.GetMainKeyboard(), cancellationToken);
                    return;

                case "⬇️ Загрузить":
                    await SendMessageAsync("Выберите опцию:", Keyboards.GetDownloadKeyboard(), cancellationToken);
                    return;

                case "⚙ Настройки":
                    await SendMessageAsync(
                        "Выбери нужный раздел настроек на клавиатуре ниже",
                        Keyboards.GetSettingsKeyboard(),
                        cancellationToken);
                    return;

                case "📂 Папка загрузки":
                    await SendMessageAsync("...", Keyboards.GetPathEditKeyboard(), cancellationToken);
                    await SendDownloadPathAsync(cancellationToken);
                    return;

                case "💾 Сохранить настройки":
                    Program.Settings.Save();
                    await SendMessageAsync(
                        "💾 <b>Настройки сохранены</b>",
                        Keyboards.GetMainKeyboard(),
                        cancellationToken);
                    return;

                case "⛔ Остановить запись":
                    var active = Program.TwitchDownloader.GetActiveDownloads();
                    if (active.Count == 0)
                    {
                        await SendMessageAsync(
                            "ℹ️ Сейчас активных записей нет",
                            Keyboards.GetMainKeyboard(),
                            cancellationToken);
                        return;
                    }

                    await SendMessageAsync(
                        "Выбери конкретную сессию. Остановка закроет сырьё и продолжит сборку MP4:",
                        Keyboards.GetStopDownloadsKeyboard(active),
                        cancellationToken);
                    return;

                default:
                    await SendMessageAsync(
                        $"Нет такой команды: <b>{Html(text)}</b>",
                        Keyboards.GetMainKeyboard(),
                        cancellationToken);
                    return;
            }
        }

        private async Task HandleCallbackAsync(
            ITelegramBotClient bot,
            CallbackQuery callback,
            CancellationToken cancellationToken)
        {
            if (Keyboards.TryParseDeleteChannelCallback(callback.Data, out var channelToDelete))
            {
                await HandleDeleteChannelCallbackAsync(bot, callback, channelToDelete, cancellationToken);
                return;
            }

            if (Keyboards.TryParseStopDownloadCallback(callback.Data, out var sessionId))
            {
                StopDownloadResult result;
                try
                {
                    result = await Program.TwitchDownloader.RequestStopAsync(sessionId, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    await bot.AnswerCallbackQuery(
                        callback.Id,
                        text: "Пауза не сохранена",
                        cancellationToken: cancellationToken);
                    await SendMessageAsync(
                        $"❌ Запись продолжена: {Html(ex.Message)}",
                        Keyboards.GetMainKeyboard(),
                        cancellationToken);
                    return;
                }
                var answer = result switch
                {
                    StopDownloadResult.Accepted => "Остановка принята",
                    StopDownloadResult.AlreadyStopping => "Сессия уже завершается",
                    _ => "Сессия уже завершена или устарела"
                };
                await bot.AnswerCallbackQuery(callback.Id, text: answer, cancellationToken: cancellationToken);
                await RefreshInlineKeyboardAsync(
                    callback,
                    Program.TwitchDownloader.GetActiveDownloads() is { Count: > 0 } remaining
                        ? Keyboards.GetStopDownloadsKeyboard(remaining)
                        : null,
                    cancellationToken);
                await SendMessageAsync(
                    result switch
                    {
                        StopDownloadResult.Accepted => "⛔ Запись остановлена, идёт сборка MP4.",
                        StopDownloadResult.AlreadyStopping => "ℹ️ Запись уже остановлена или MP4 уже собирается.",
                        _ => "⚠️ Эта кнопка устарела: соответствующей сессии больше нет."
                    },
                    Keyboards.GetMainKeyboard(),
                    cancellationToken);
                return;
            }

            switch (callback.Data)
            {
                case "info":
                    await SendMessageAsync("Это информация о сервисе 🧠", cancellationToken: cancellationToken);
                    break;
                case "settings":
                    await SendMessageAsync("Здесь будут настройки ⚙", cancellationToken: cancellationToken);
                    break;
                case "editdownloadpath":
                    DisableTriggers();
                    _editDownloadPathTrigger = true;
                    await SendMessageAsync(
                        "Введи новый путь к папке для загрузки стримов",
                        Keyboards.GetOnlyCancelKeyboard(),
                        cancellationToken);
                    break;
            }

            await bot.AnswerCallbackQuery(callback.Id, cancellationToken: cancellationToken);
        }

        private async Task HandleDeleteChannelCallbackAsync(
            ITelegramBotClient bot,
            CallbackQuery callback,
            string channel,
            CancellationToken cancellationToken)
        {
            DisableTriggers();
            var removed = Program.Settings.RemoveTrackedChannel(channel);
            if (removed)
                Program.Settings.Save();

            await bot.AnswerCallbackQuery(
                callback.Id,
                text: removed ? "Канал удалён" : "Канала уже нет в списке",
                cancellationToken: cancellationToken);

            var remaining = Program.Settings.GetTrackedChannelsSnapshot();
            await RefreshInlineKeyboardAsync(
                callback,
                remaining.Count > 0 ? Keyboards.GetDeleteChannelsKeyboard(remaining) : null,
                cancellationToken);
            await SendMessageAsync(
                removed
                    ? $"🗑️ Канал <b>{Html(channel)}</b> удалён"
                    : $"⚠️ Канала <b>{Html(channel)}</b> уже нет в отслеживаемых",
                Keyboards.GetChannelsKeyboard(),
                cancellationToken);
        }

        // Обновляет кнопки под исходным сообщением, чтобы в нём не оставались устаревшие варианты.
        private async Task RefreshInlineKeyboardAsync(
            CallbackQuery callback,
            InlineKeyboardMarkup? replyMarkup,
            CancellationToken cancellationToken)
        {
            if (callback.Message is not { } source)
                return;

            try
            {
                await _bot.EditMessageReplyMarkup(
                    source.Chat.Id,
                    source.MessageId,
                    replyMarkup,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Telegram отклоняет правку без изменений или слишком старого сообщения — это не мешает действию.
                ConsoleWriteLine($"Не удалось обновить кнопки сообщения: {ex.Message}", ConsoleColor.DarkYellow);
            }
        }

        private async Task SendHistoryAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<RecordingHistoryEntry> entries;
            int total;
            try
            {
                entries = Program.History.GetRecent(HistoryPageSize);
                total = Program.History.Count();
            }
            catch (Exception ex)
            {
                await SendMessageAsync(
                    $"❌ Не удалось прочитать историю записей: {Html(ex.Message)}",
                    Keyboards.GetMainKeyboard(),
                    cancellationToken);
                return;
            }

            if (entries.Count == 0)
            {
                await SendMessageAsync("🏺 История записей пока пуста", Keyboards.GetMainKeyboard(), cancellationToken);
                return;
            }

            var text = $"---- 🏺 История записей ----\nПоследние {entries.Count} из {total}\n\n" +
                       string.Join("\n\n", entries.Select(FormatHistoryEntry));
            await SendMessageAsync(text, Keyboards.GetMainKeyboard(), cancellationToken);
        }

        internal static string FormatHistoryEntry(RecordingHistoryEntry entry)
        {
            var startedAt = entry.StartedAt.ToLocalTime();
            var endedAt = entry.RecordingEndedAt.ToLocalTime();
            var endDate = endedAt.Date == startedAt.Date ? string.Empty : $"{endedAt:dd.MM.yyyy} ";
            var status = entry.Succeeded
                ? entry.StoppedManually ? "⛔ остановлена вручную" : "✅ завершена"
                : "⚠️ ошибка";
            var file = entry.Succeeded && entry.OutputPath is not null
                ? $"\n📂 <code>{Html(entry.OutputPath)}</code>"
                : $"\nПричина: {Html(Truncate(entry.ErrorMessage ?? "неизвестная ошибка", 200))}";
            return $"🎥 <b>{Html(entry.Channel)}</b> · {status}\n" +
                   $"📅 {startedAt:dd.MM.yyyy} · {startedAt:HH:mm} – {endDate}{endedAt:HH:mm}\n" +
                   $"🕓 Общая длительность: <b>{FormatDuration((entry.RecordingEndedAt - entry.StartedAt).TotalSeconds)}</b>" +
                   $" · видео: <b>{FormatDuration(entry.DurationSeconds)}</b>\n" +
                   $"💾 {FormatSize(entry.SizeBytes)} · 📢 реклама: {entry.AdvertisementCount} · 🕳 дыр: {entry.GapCount}" +
                   file;
        }

        public Task RecordingStartedAsync(RecordingStartedInfo info, CancellationToken cancellationToken)
        {
            var quality = string.IsNullOrWhiteSpace(info.SelectedQuality) ? "неизвестно" : Html(info.SelectedQuality);
            var fallback = info.IsSource
                ? string.Empty
                : "\n⚠️ Source недоступен, выбран первый вариант.";
            return SendMessageAsync(
                $"✨ У <b>{Html(info.Download.Channel)}</b> началась трансляция!\n\n" +
                $"⬇️ Запись запущена · качество: <b>{quality}</b>{fallback}\n\n" +
                $"📂 Сырьё: <code>{Html(info.Download.Path)}</code>",
                cancellationToken: cancellationToken);
        }

        public Task RecordingEndedAsync(RecordingEndedInfo info, CancellationToken cancellationToken)
        {
            var processingMessage = info.HasContent
                ? "⚙️ Обработка записи началась."
                : "⚠️ Медиа-сегменты не получены; результат будет отправлен отдельным уведомлением.";
            return SendMessageAsync(
                $"🏁 Трансляция <b>{Html(info.Channel)}</b> завершена.\n\n" +
                $"{processingMessage}\n" +
                $"⏱ Записано: <b>{FormatDuration(info.DurationSeconds)}</b>",
                cancellationToken: cancellationToken);
        }

        public Task RecordingCompletedAsync(RecordingCompletionInfo info, CancellationToken cancellationToken)
        {
            if (info.Succeeded && info.OutputPath is not null)
            {
                return SendMessageAsync(
                    $"✅ Обработка записи для <b>{Html(info.Channel)}</b> завершена. MP4 готов.\n\n" +
                    $"📂 <code>{Html(info.OutputPath)}</code>\n" +
                    $"⏱ Длительность: <b>{FormatDuration(info.DurationSeconds)}</b>\n" +
                    $"💾 Размер: <b>{FormatSize(info.SizeBytes)}</b>\n" +
                    $"📢 Пропущено рекламы: <b>{info.AdvertisementCount}</b> ({FormatDuration(info.AdvertisementDurationSeconds)})\n" +
                    $"🕳 Дыр в HLS: <b>{info.GapCount}</b>",
                    cancellationToken: cancellationToken);
            }

            var candidate = info.OutputPath is null
                ? string.Empty
                : $"\nКандидат MP4: <code>{Html(info.OutputPath)}</code>";
            return SendMessageAsync(
                $"⚠️ Обработка записи для <b>{Html(info.Channel)}</b> завершена с ошибкой.\n" +
                $"Проверенный MP4 не собран.\n" +
                $"Сырьё сохранено: <code>{Html(info.RawPath)}</code>{candidate}\n" +
                $"⏱ Длительность: <b>{FormatDuration(info.DurationSeconds)}</b>\n" +
                $"💾 Размер failure-артефакта: <b>{FormatSize(info.SizeBytes)}</b>\n" +
                $"📢 Пропущено рекламы: <b>{info.AdvertisementCount}</b> ({FormatDuration(info.AdvertisementDurationSeconds)})\n" +
                $"🕳 Дыр в HLS: <b>{info.GapCount}</b>\n" +
                $"Причина: {Html(info.ErrorMessage ?? "неизвестная ошибка")}",
                cancellationToken: cancellationToken);
        }

        public async Task SendMessageAsync(
            string text,
            ReplyMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default,
            ParseMode parseMode = ParseMode.Html)
        {
            await _bot.SendMessage(
                chatId: _ownerId,
                text: text,
                parseMode: parseMode,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true });
        }

        public Task SendNotification(string text)
        {
            return SendMessageAsync(text);
        }

        private async Task SendStartMessageAsync(Message message, CancellationToken cancellationToken)
        {
            DisableTriggers();
            await SendMessageAsync(
                $"Привет, {Html(message.Chat.FirstName)} {Html(message.Chat.LastName)}",
                Keyboards.GetMainKeyboard(),
                cancellationToken);
            await SendMessageAsync(MainPageString(), Keyboards.GetMainKeyboard(), cancellationToken);
        }

        private Task SendDownloadPathAsync(CancellationToken cancellationToken)
        {
            var path = Program.Settings.DownloadPath.Replace(@"\", @"\\");
            return SendMessageAsync(
                $"**📂 Папка загрузки**\n\nСейчас загрузка происходит в папку по такому пути:\n```path\n{path}```",
                Keyboards.GetEditPathButton(),
                cancellationToken,
                ParseMode.MarkdownV2);
        }

        private void DisableTriggers()
        {
            _addChannelTrigger = false;
            _editDownloadPathTrigger = false;
        }

        private static string ExtractChannelName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            input = input.Trim();
            if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                input = input[8..];
            else if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                input = input[7..];
            if (input.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                input = input[4..];
            if (input.StartsWith("twitch.tv/", StringComparison.OrdinalIgnoreCase))
                input = input["twitch.tv/".Length..];

            var separator = input.IndexOfAny(['/', '?', '&']);
            return separator >= 0 ? input[..separator] : input;
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength] + "…";

        private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string FormatDuration(double seconds)
        {
            var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }

        private static string FormatSize(long bytes)
        {
            string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
            var value = Math.Max(0, bytes);
            var unit = 0;
            var display = (double)value;
            while (display >= 1024 && unit < units.Length - 1)
            {
                display /= 1024;
                unit++;
            }
            return $"{display:0.##} {units[unit]}";
        }

        private static string MainPageString()
        {
            return "------------ Общая информация о работе ------------\n\n" +
                   $"🕓 Аптайм: {Program.Uptime}\n" +
                   $"📺 Каналы: {Program.Settings.GetTrackedChannelsSnapshot().Count}";
        }

        private static void ConsoleWriteLine(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("[");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("Telegram");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = previousColor;
        }
    }
}
