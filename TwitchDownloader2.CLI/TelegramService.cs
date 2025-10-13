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
                        await SendMessageAsync($"❌ <b>Действие отменено</b>", replyMarkup: GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                        return;
                    }
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        if (_addChannelTrigger)
                        {
                            if (Program.Settings.TrackedChannels.Contains(ExtractChannelName(message.Text.Replace(" ", "").ToLower())))
                            {
                                await SendMessageAsync($"⚠️ Канал <b>{ExtractChannelName(message.Text.Replace(" ", "").ToLower())}</b> уже был добавлен ранее", replyMarkup: GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                                disableTriggers();
                                return;
                            }
                            Program.Settings.TrackedChannels.Add(ExtractChannelName(message.Text.Replace(" ", "").ToLower()));
                            Program.Settings.Save();
                            disableTriggers();
                            await SendMessageAsync($"✨ Канал <b>{ExtractChannelName(message.Text.Replace(" ", "").ToLower())}</b> добавлен в отслеживаемые", replyMarkup: GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                            return;
                        }
                        if (_deleteChannelTrigger)
                        {
                            if (!Program.Settings.TrackedChannels.Contains(ExtractChannelName(message.Text.Replace(" ", "").ToLower())))
                            {
                                await SendMessageAsync($"⚠️ Такой канал <b>{ExtractChannelName(message.Text.Replace(" ", "").ToLower())}</b> отсутствует", replyMarkup: GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                                disableTriggers();
                                return;
                            }
                            Program.Settings.TrackedChannels.Remove(message.Text.Replace(" ", ""));
                            Program.Settings.Save();
                            disableTriggers();
                            await SendMessageAsync($"🗑️ Канал <b>{ExtractChannelName(message.Text.Replace(" ", "").ToLower())}</b> удален", replyMarkup: GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                            return;
                        }
                    }
                    if (message.Text.StartsWith("/start"))
                    {
                        disableTriggers();
                        await SendMessageAsync($"Привет, {message.Chat.FirstName} {message.Chat.LastName}", replyMarkup: GetMainKeyboard(), cancellationToken: token);
                        await SendMessageAsync(MainPageString(), replyMarkup: GetMainKeyboard(), cancellationToken: token);
                        return;
                    }
                    if (message.Text == "➕ Добавить")
                    {
                        disableTriggers();
                        _addChannelTrigger = true;
                        await SendMessageAsync($"Напиши имя канала или ссылку на Twitch", replyMarkup: GetOnlyCancelKeyboard("Вставить ссылку на Twitch сюда"), cancellationToken: token);
                        return;
                    }
                    if (message.Text == "🗑️ Удалить")
                    {
                        disableTriggers();
                        _deleteChannelTrigger = true;
                        await SendMessageAsync($"Напиши имя канала или ссылку на Twitch", replyMarkup: GetDynamicKeyboard(Program.Settings.TrackedChannels, "Вставить ссылку на Twitch сюда"), cancellationToken: token);
                        return;
                    }
                    if (message.Text == "📺 Каналы")
                    {
                        var channels = "---- Отслеживаемые каналы на Twitch ----\n";
                        channels += "<b>" + string.Join("\n", Program.Settings.TrackedChannels.Select(ch => $"🎥 <a href=\"https://www.twitch.tv/{ch}\">{ch}</a>")) + "</b>";
                        await SendMessageAsync(channels, replyMarkup: GetMainKeyboard(), parseMode: ParseMode.Html, cancellationToken: token);
                        disableTriggers();
                        return;
                    }
                    if (message.Text == "🏠 Главная")
                    {

                    }
                    else if (message.Text == "/buttons")
                    {
                        var buttons = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🧠 Инфо", "info"),
                                InlineKeyboardButton.WithCallbackData("⚙ Настройки", "settings")
                            }
                        });

                        await SendMessageAsync("Выберите опцию:", buttons, token);
                    }
                    else
                    {
                        await SendMessageAsync($"Нет такой команды: <b>{message.Text}</b>", parseMode: ParseMode.Html, cancellationToken: token);
                    }
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

        /// <summary>
        /// Пример клавиатуры под полем ввода текста.
        /// </summary>
        private static ReplyKeyboardMarkup GetMainKeyboard(string placeholder = "Используй кнопки ниже")
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "📺 Каналы", "➕ Добавить", "🗑️ Удалить" },
                new KeyboardButton[] { "📜 Статус", "🏺 История", "⬇️ Загрузить" },
                new KeyboardButton[] { "🏠 Главная", "⚙ Настройки" },
                new KeyboardButton[] { "🔁 Принудительно обновить" }
            })
            {
                InputFieldPlaceholder = placeholder,
                IsPersistent = true,
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }
        private static ReplyKeyboardMarkup GetOnlyCancelKeyboard(string placeholder = "Введи это сюда")
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "❌ Отменить действие" }
            })
            {
                InputFieldPlaceholder = placeholder,
                IsPersistent = true,
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }
        private static ReplyKeyboardMarkup GetServiceKeyboard(string placeholder = "Введи это сюда")
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "❌ Отменить действие" },
                new KeyboardButton[] { "📺 Каналы", "➕ Добавить", "🗑️ Удалить" },
                new KeyboardButton[] { "📜 Статус", "🏺 История", "⬇️ Загрузить" },
                new KeyboardButton[] { "🏠 Главная", "⚙ Настройки" },
                new KeyboardButton[] { "🔁 Принудительно обновить" }
            })
            {
                InputFieldPlaceholder = placeholder,
                IsPersistent = true,
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }

        private static ReplyKeyboardMarkup GetDynamicKeyboard(IEnumerable<string> items, string placeholder = "Введи имя канала тут или выбери ниже")
        {
            const int maxButtonsPerRow = 4;

            var rows = items
            .Select((text, index) => new { text, index })
            .GroupBy(x => x.index / maxButtonsPerRow)
            .Select(g => g.Select(x => new KeyboardButton(x.text)).ToArray())
            .ToList();

            rows.Insert(0, new[] { new KeyboardButton("❌ Отменить действие") });

            return new ReplyKeyboardMarkup(rows)
            {
                InputFieldPlaceholder = placeholder,
                IsPersistent = true,
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }

        private string MainPageString()
        {
            return $"" +
                    $"------------ Общая информация о работе ------------\n\n" +
                    $"🕓 Аптайм: {Program.Uptime}\n" +
                    $"📺 Каналы: {Program.Settings.TrackedChannels.Count}";
        }
    }
}
