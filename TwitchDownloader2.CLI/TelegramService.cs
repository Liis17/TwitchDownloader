using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TwitchDownloader2.CLI
{
    public class TelegramService
    {
        private readonly TelegramBotClient _bot;
        private readonly long _ownerId;
        private CancellationTokenSource? _cts;
        private string _serviceName = "Telegram";
        private ConsoleColor _consoleColor = ConsoleColor.Blue;

        private bool _addChannelTrigger = false;
        private bool _deleteChannelTrigger = false;
        private bool _editDownloadPathTrigger = false;

        #region Служебные методы
        public TelegramService(string token, long ownerId)
        {
            _bot = new TelegramBotClient(token);
            _ownerId = ownerId;
        }

        /// <summary>
        /// Запускает Telegram-сервис в отдельном потоке.
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();

            Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>
        /// Останавливает Telegram-сервис.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Запускает приём апдейтов от Telegram.
        /// </summary>
        /// <param name="token">Токен бота</param>
        /// <returns></returns>
        private async Task RunAsync(CancellationToken token)
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // получать все типы апдейтов
            };

            _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, token);

            var me = await _bot.GetMe(token);
            ConsoleWriteLine($"✅ Telegram bot запущен как @{me.Username}", ConsoleColor.Gray);
        }

        private void ConsoleWriteLine(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("[");
            Console.ForegroundColor = _consoleColor;
            Console.Write($"{_serviceName}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = previousColor;
        }

        private string ExtractChannelName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            input = input.Trim();

            if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(8);
            else if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(7);

            if (input.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(4);

            if (input.StartsWith("twitch.tv/", StringComparison.OrdinalIgnoreCase))
                input = input.Substring("twitch.tv/".Length);

            int slashIndex = input.IndexOfAny(new[] { '/', '?', '&' });
            if (slashIndex >= 0)
                input = input.Substring(0, slashIndex);

            return input;
        }
        private Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken token)
        {
            ConsoleWriteLine($"Telegram Error: {exception.Message}", ConsoleColor.DarkRed);
            return Task.CompletedTask;
        }
        #endregion

        private void disableTriggers()
        {
            _addChannelTrigger = false;
            _deleteChannelTrigger = false;
            _editDownloadPathTrigger = false;
        }
        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
        {
            if (update.Message is { } message)
            {
                if (message.From == null || message.From.Id != _ownerId)
                {
                    // Игнорировать чужие сообщения
                    return;
                }

                if (message.Text != null)
                {
                    #region Консольный вывод лога
                    var sender = "";
                    if (string.IsNullOrEmpty(message.From.Username))
                    {
                        sender = message.From.Id.ToString();
                    }
                    sender = message.From.Username;
                    ConsoleWriteLine($"{sender}: {message.Text}");
                    #endregion

                    if (message.Text == "❌ Отменить действие")
                    {
                        disableTriggers();
                        await SendMessageAsync($"❌ <b>Действие отменено</b>", replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                        return;
                    }
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        if (_addChannelTrigger)
                        {
                            if (Program.Settings.TrackedChannels.Contains(ExtractChannelName(message.Text.Replace(" ", "").ToLower())))
                            {
                                await SendMessageAsync($"⚠️ Канал <b>{ExtractChannelName(message.Text.Replace(" ", "").ToLower())}</b> уже был добавлен ранее", replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                                disableTriggers();
                                return;
                            }
                            Program.Settings.TrackedChannels.Add(ExtractChannelName(message.Text.Replace(" ", "").ToLower()));
                            Program.Settings.Save();
                            disableTriggers();
                            await SendMessageAsync($"✨ Канал <b>{ExtractChannelName(message.Text.Replace(" ", "").ToLower())}</b> добавлен в отслеживаемые", replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                            return;
                        }
                        if (_deleteChannelTrigger)
                        {
                            if (!Program.Settings.TrackedChannels.Contains(ExtractChannelName(message.Text.Replace(" ", "").ToLower())))
                            {
                                await SendMessageAsync($"⚠️ Такой канал <b>{ExtractChannelName(message.Text.Replace(" ", "").ToLower())}</b> отсутствует", replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                                disableTriggers();
                                return;
                            }
                            Program.Settings.TrackedChannels.Remove(message.Text.Replace(" ", ""));
                            Program.Settings.Save();
                            disableTriggers();
                            await SendMessageAsync($"🗑️ Канал <b>{ExtractChannelName(message.Text.Replace(" ", "").ToLower())}</b> удален", replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                            return;
                        }
                        if (_editDownloadPathTrigger)
                        {
                            if (!Directory.Exists(message.Text))
                            {
                                await SendMessageAsync($"❌ Такой путь не найден", replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                            }
                            else
                            {
                                Program.Settings.DownloadPath = message.Text;
                                await SendMessageAsync($"✨ Путь изменен", replyMarkup: Keyboards.GetPathEditKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                                var path = Program.Settings.DownloadPath.Replace(@"\", @"\\");
                                Program.Settings.Save();
                                await SendMessageAsync($"**📂 Папка загрузки**\n\nСейчас загрузка происходит в папку по такому пути:\n```path\n{path}```", Keyboards.GetEditPathButton(), token, parseMode: ParseMode.MarkdownV2);
                            }
                            disableTriggers();
                            return;
                        }
                    }
                    if (message.Text.StartsWith("/start"))
                    {
                        _startMessage();
                        return;
                    }
                    if (message.Text == "➕ Добавить")
                    {
                        disableTriggers();
                        _addChannelTrigger = true;
                        await SendMessageAsync($"Напиши имя канала или ссылку на Twitch", replyMarkup: Keyboards.GetOnlyCancelKeyboard("Вставить ссылку на Twitch сюда"), cancellationToken: token);
                        return;
                    }
                    if (message.Text == "🗑️ Удалить")
                    {
                        disableTriggers();
                        _deleteChannelTrigger = true;
                        await SendMessageAsync($"Напиши имя канала который хочешь удалить", replyMarkup: Keyboards.GetDynamicKeyboard(Program.Settings.TrackedChannels, "Можешь выбрать на кнопках ниже"), cancellationToken: token);
                        return;
                    }
                    if (message.Text == "📺 Каналы")
                    {
                        var channels = "---- Отслеживаемые каналы на Twitch ----\n";
                        channels += "<b>" + string.Join("\n", Program.Settings.TrackedChannels.Select(ch => $"🎥 <a href=\"https://www.twitch.tv/{ch}\">{ch}</a>")) + "</b>";
                        await SendMessageAsync(channels, replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                        disableTriggers();
                        return;
                    }
                    if (message.Text == "🏠 Главная" || message.Text == "🏠 Вернуться на главную")
                    {
                        _startMessage();
                        return;
                    }
                    if (message.Text == "🔁 Принудительно обновить")
                    {
                        Program.TwitchChecker.ForceCheck();
                        await SendMessageAsync($"<b>Выполнено</b>", parseMode: ParseMode.Html, replyMarkup: Keyboards.GetMainKeyboard(), cancellationToken: token);
                        return;
                    }
                    if (message.Text == "📜 Статус")
                    {
                        var list = Program.TwitchChecker.GetStatuses();

                        string text = "---- Статус отслеживаемых каналов ----\n\n";
                        foreach (var channel in list)
                        {
                            var status = "";
                            if (channel.Value)
                            {
                                status = "🔴";
                            }
                            else
                            {
                                status = "💤";
                            }
                            text += $"{status} {channel.Key}" + "\n";
                        }

                        await SendMessageAsync(text, replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                        return;
                    }
                    if (message.Text == "⬇️ Загрузить")
                    {

                        await SendMessageAsync("Выберите опцию:", Keyboards.GetDownloadKeyboard(), token, parseMode: ParseMode.Html);
                        return;
                    }
                    if (message.Text == "⚙ Настройки")
                    {
                        await SendMessageAsync("Чтобы продолжить нужно выбрать нужный раздел настроек на клавиатуре ниже", Keyboards.GetSettingsKeyboard(), token, parseMode: ParseMode.Html);
                        return;
                    }
                    if (message.Text == "📂 Папка загрузки")
                    {
                        await SendMessageAsync($"...", Keyboards.GetPathEditKeyboard(), token, parseMode: ParseMode.Html);
                        var path = Program.Settings.DownloadPath.Replace(@"\", @"\\");
                        await SendMessageAsync($"**📂 Папка загрузки**\n\nСейчас загрузка происходит в папку по такому пути:\n```path\n{path}```", Keyboards.GetEditPathButton(), token, parseMode: ParseMode.MarkdownV2);
                        return;
                    }
                    if (message.Text == "💾 Сохранить настройки")
                    {
                        await SendMessageAsync($"**💾 Настройки сохранены**", Keyboards.GetMainKeyboard(), token, parseMode: ParseMode.MarkdownV2);
                        Program.Settings.Save();
                        return;
                    }
                    if (message.Text == "[placeholder]")
                    {

                        await SendMessageAsync("Действие еще не реализованно, можете проверить обновление на <b><a href=\"https://я.проебал.домен/app/twitchdownloader\">сайте</a></b>", Keyboards.GetMainKeyboard(), token, parseMode: ParseMode.Html);
                        return;
                    }
                    else
                    {
                        await SendMessageAsync($"Нет такой команды: <b>{message.Text}</b>", replyMarkup: Keyboards.GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                    }
                }

                async void _startMessage()
                {
                    disableTriggers();
                    await SendMessageAsync($"Привет, {message.Chat.FirstName} {message.Chat.LastName}", replyMarkup: Keyboards.GetMainKeyboard(), cancellationToken: token);
                    await SendMessageAsync(MainPageString(), replyMarkup: Keyboards.GetMainKeyboard(), cancellationToken: token);
                }
            }
            else if (update.CallbackQuery is { } callback)
            {
                if (callback.From.Id != _ownerId) return;

                switch (callback.Data)
                {
                    case "info":
                        await SendMessageAsync("Это информация о сервисе 🧠", cancellationToken: token);
                        break;
                    case "settings":
                        await SendMessageAsync("Здесь будут настройки ⚙", cancellationToken: token);
                        break;
                    case "editdownloadpath":
                        await SendMessageAsync("Введи новый путь к папке для загрузки стримов", Keyboards.GetOnlyCancelKeyboard(), token, ParseMode.Html);
                        _editDownloadPathTrigger = true;
                        break;
                }

                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: token);
            }
        }

        /// <summary>
        /// Отправка сообщения владельцу.
        /// </summary>
        public async Task SendMessageAsync(string text, ReplyMarkup? replyMarkup = null, CancellationToken cancellationToken = default, ParseMode parseMode = ParseMode.Html)
        {
            var linkPreview = new LinkPreviewOptions();
            linkPreview.IsDisabled = true;
            await _bot.SendMessage(
                chatId: _ownerId,
                text: text,
                parseMode: parseMode,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken,
                linkPreviewOptions: linkPreview
            );
        }

        public async Task SendNotification(string text)
        {
            await SendMessageAsync(text, parseMode: ParseMode.Html);
        }

        /// <summary>
        /// Пример клавиатуры под полем ввода текста.
        /// </summary>


        private string MainPageString()
        {
            return $"" +
                    $"------------ Общая информация о работе ------------\n\n" +
                    $"🕓 Аптайм: {Program.Uptime}\n" +
                    $"📺 Каналы: {Program.Settings.TrackedChannels.Count}";
        }
    }
}
