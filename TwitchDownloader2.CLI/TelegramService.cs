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

        private bool _addChannelTrigger;
        private bool _deleteChannelTrigger;
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
                        Keyboards.GetMainKeyboard(),
                        cancellationToken);
                    return;
                }

                Program.Settings.Save();
                Program.TwitchChecker.ForceCheck();
                await SendMessageAsync(
                    $"✨ Канал <b>{Html(channel)}</b> добавлен в отслеживаемые",
                    Keyboards.GetMainKeyboard(),
                    cancellationToken);
                return;
            }

            if (_deleteChannelTrigger)
            {
                var channel = ExtractChannelName(text.Replace(" ", string.Empty).ToLowerInvariant());
                DisableTriggers();
                if (!Program.Settings.RemoveTrackedChannel(channel))
                {
                    await SendMessageAsync(
                        $"⚠️ Такой канал <b>{Html(channel)}</b> отсутствует",
                        Keyboards.GetMainKeyboard(),
                        cancellationToken);
                    return;
                }

                Program.Settings.Save();
                await SendMessageAsync(
                    $"🗑️ Канал <b>{Html(channel)}</b> удалён",
                    Keyboards.GetMainKeyboard(),
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
                    _deleteChannelTrigger = true;
                    await SendMessageAsync(
                        "Напиши имя канала, который хочешь удалить",
                        Keyboards.GetDynamicKeyboard(
                            Program.Settings.GetTrackedChannelsSnapshot(),
                            "Можешь выбрать на кнопках ниже"),
                        cancellationToken);
                    return;

                case "📺 Каналы":
                    var trackedChannels = Program.Settings.GetTrackedChannelsSnapshot();
                    var channels = "---- Отслеживаемые каналы на Twitch ----\n";
                    channels += "<b>" + string.Join(
                        '\n',
                        trackedChannels.Select(channel =>
                            $"🎥 <a href=\"https://www.twitch.tv/{Html(channel)}\">{Html(channel)}</a>")) + "</b>";
                    await SendMessageAsync(channels, Keyboards.GetMainKeyboard(), cancellationToken);
                    DisableTriggers();
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

                case "[placeholder]":
                    await SendMessageAsync(
                        "Действие ещё не реализовано",
                        Keyboards.GetMainKeyboard(),
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

        public Task RecordingCompletedAsync(RecordingCompletionInfo info, CancellationToken cancellationToken)
        {
            if (info.Succeeded && info.OutputPath is not null)
            {
                return SendMessageAsync(
                    $"✅ MP4 для <b>{Html(info.Channel)}</b> готов.\n\n" +
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
                $"⚠️ Не удалось собрать проверенный MP4 для <b>{Html(info.Channel)}</b>.\n" +
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
            _deleteChannelTrigger = false;
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
