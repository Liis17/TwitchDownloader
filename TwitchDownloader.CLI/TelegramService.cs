using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types;
using Telegram.Bot;
using Telegram.Bot.Polling;
using System.Threading;
using File = System.IO.File;

namespace TwitchDownloader.CLI
{
    public class TelegramService
    {
        private string BotToken = "123:abcd";
        public long AdminId = 123;
        public TelegramBotClient botClient;
        private bool _waitingForLink = false;
        private bool _waitingPlayer = false;
        private bool _waitingForChannel = false;
        private string _trackedChannel = null; // Отслеживаемый канал
        private readonly string _filePath = "trackable.user"; // Путь к файлу
        private string downloader = string.Empty;

        public async Task StartBotAsync(string token, string id)
        {
            BotToken = token;
            AdminId = int.Parse(id);

            botClient = new TelegramBotClient(BotToken);
            LoadTrackedChannel(); // Загрузка канала из файла при старте

            using var cts = new CancellationTokenSource();
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };
            StartTrackingChannel(); // Запуск задачи отслеживания канала
            while (true)
            {
                try
                {
                    botClient.StartReceiving(
                        HandleUpdateAsync,
                        HandleErrorAsync,
                        receiverOptions,
                        cts.Token
                    );

                    var me = await botClient.GetMeAsync();
                    Console.WriteLine($"Запущен бот {me.Username}");

                    Console.ReadLine();
                    cts.Cancel();

                    await Task.Delay(-1, cts.Token); // Ожидание завершения работы
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Критическая ошибка: {ex.Message}. Перезапуск через 5 секунд...");
                    await Task.Delay(5000); // Задержка перед перезапуском
                }
            }

            
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text)
            {
                var message = update.Message;

                if (message.From.Id == AdminId)
                {
                    if (message.Text == "/start")
                    {
                        await ShowMainMenu(botClient, message.Chat.Id, cancellationToken, message.Chat.FirstName + " " + message.Chat.LastName);
                    }
                    else if (_waitingForLink)
                    {
                        if (Uri.IsWellFormedUriString(message.Text, UriKind.Absolute))
                        {
                            await SaveVideo(message.Text);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat.Id, "Некорректный формат ссылки. Попробуйте снова.", cancellationToken: cancellationToken);
                        }
                        _waitingForLink = false;
                    }
                    else if (_waitingForChannel)
                    {
                        _trackedChannel = message.Text;
                        SaveTrackedChannel(); 
                        _waitingForChannel = false;
                        await botClient.SendTextMessageAsync(message.Chat.Id, $"Канал {message.Text} добавлен в отслеживаемые.", messageEffectId: "5046509860389126442", cancellationToken: cancellationToken);
                        StartTrackingChannel();
                        await ShowMainMenu(botClient, message.Chat.Id, cancellationToken);
                    }
                    else if (_waitingPlayer)
                    {
                        if (Uri.IsWellFormedUriString(message.Text, UriKind.Absolute))
                        {
                            await PlayVideo(message.Text);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat.Id, "Некорректный формат ссылки.", cancellationToken: cancellationToken);
                        }
                        _waitingPlayer = false;
                    }
                    else 
                    {
                        await botClient.SendTextMessageAsync(message.Chat.Id, $"Что это ???\nСейчас я не жду от тебя этого:\n\n{message.Text}\n\nВыбери действие в /start\nИ действую по шагам что я спрошу!", cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "Управление ботом доступно только для его владельца!",
                        cancellationToken: cancellationToken
                    );
                }
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                var callbackQuery = update.CallbackQuery;

                if (callbackQuery.From.Id == AdminId)
                {
                    var cancelKeyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Отменить", "cancel"));
                    switch (callbackQuery.Data)
                    {
                        case "download":
                            var downloaderbutton = new[]
                                {
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("🛠️ ffmpeg", "defaultffmpeg"),
                                    InlineKeyboardButton.WithCallbackData("🛠️ ffmpeg + 🔊 audio", "experementalfixaudio")
                                },
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("🤪 Больше вариантов", "otherdownloaders")
                                },
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("Отменить", "cancel"),
                                }
                             };
                            var downloaderkeyboard = new InlineKeyboardMarkup(downloaderbutton);
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Выберите загрузчик\n Для скачивания клипов и завершенных трансляций подходит [ ffmpeg ].\nДля активной трансляции [ ffmpeg + audio ] ", replyMarkup: downloaderkeyboard, cancellationToken: cancellationToken);
                            break;

                        case "otherdownloaders":
                            var otherdownloaderbutton = new[]
                                {
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("🛠️ ffmpeg", "defaultffmpeg"),
                                    InlineKeyboardButton.WithCallbackData("⌛ ffmpeg с задержкой получения", "ffmpegrw_timeout"),
                                    InlineKeyboardButton.WithCallbackData("🎬 yt-dlp", "ytdlp")
                                },
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("⏱️ ffmpeg с временным буфером", "ffmpegbuffer"),
                                    InlineKeyboardButton.WithCallbackData("📝 ffmpeg с задержкой записи", "ffmpegwallclock"),
                                    InlineKeyboardButton.WithCallbackData("🧪 experemental ffmpeg", "experemental")
                                },
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("🧪 exp ffmpeg fix audio", "experementalfixaudio")
                                },
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("🎥 Открыть плеер", "open_player")
                                },
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("Отменить", "cancel"),
                                }
                             };
                            var otherdownloaderkeyboard = new InlineKeyboardMarkup(otherdownloaderbutton);
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Выберите альтернативный загрузчик если другие не работают (не рекомендуется тут что то выбирать)", replyMarkup: otherdownloaderkeyboard, cancellationToken: cancellationToken);
                            break;
                        case "defaultffmpeg":
                            _waitingForLink = true;
                            downloader = "defaultffmpeg";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, $"Введи ссылку на видео Twitch. Загрузчик {downloader}.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "ffmpegrw_timeout":
                            _waitingForLink = true;
                            downloader = "ffmpegrw_timeout";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, $"Введи ссылку на видео Twitch. Загрузчик {downloader}.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "ffmpegbuffer":
                            _waitingForLink = true;
                            downloader = "ffmpegbuffer";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, $"Введи ссылку на видео Twitch. Загрузчик {downloader}.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "ffmpegwallclock":
                            _waitingForLink = true;
                            downloader = "ffmpegwallclock";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, $"Введи ссылку на видео Twitch. Загрузчик {downloader}.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "ytdlp":
                            _waitingForLink = true;
                            downloader = "ytdlp";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, $"Введи ссылку на видео Twitch. Загрузчик {downloader}.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "experemental":
                            _waitingForLink = true;
                            downloader = "experemental";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, $"Введи ссылку на видео Twitch. Загрузчик {downloader}.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "experementalfixaudio":
                            _waitingForLink = true;
                            downloader = "experementalfixaudio";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, $"Введи ссылку на видео Twitch. Загрузчик {downloader}.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;

                        case "open_player":
                            _waitingPlayer = true;
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, $"Введи ссылку на видео Twitch. \nНа хост машине быдет открыто окно ffplay", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;


                        case "track_channel":
                            _waitingForChannel = true;
                            var cancelTrackKeyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Отменить", "cancel"));
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введите имя канала Twitch (без ссылки). ", replyMarkup: cancelTrackKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "cancel":
                            _waitingForLink = false;
                            _waitingForChannel = false;
                            _waitingPlayer = false;
                            await ShowMainMenu(botClient, callbackQuery.Message.Chat.Id, cancellationToken);
                            break;
                    }
                }
            }
        }

        public async void SendMessage(string text, string EffectId = null)
        {
            await botClient.SendTextMessageAsync(AdminId, text, parseMode: ParseMode.Markdown, cancellationToken: new CancellationToken(), messageEffectId: EffectId);
        }


        private async Task ShowMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken, string username = "")
        {
            var buttons = new[]
            {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬇️ Скачать", "download"),
                InlineKeyboardButton.WithCallbackData("👁️ Отслеживать", "track_channel")
            }
        };

            var keyboard = new InlineKeyboardMarkup(buttons);
            var trackingInfo = _trackedChannel != null
                ? $"\n\nОтслеживаемый канал: {_trackedChannel}"
                : "\n\nНет отслеживаемого канала.";
            await botClient.SendTextMessageAsync(chatId, $"Привет {username}\n\nДля загрузки видео с Twitch нажми на кнопку ниже", replyMarkup: keyboard, cancellationToken: cancellationToken);
        }

        private async Task SaveVideo(string link)
        {
            SendMessage($"Загрузка с помощью {downloader}, ожидайте...");
            var a = downloader;
            downloader = string.Empty;
            Program.downloadService.StartDownload(link, a);
        }
        private async Task PlayVideo(string link)
        {
            SendMessage($"Открытие ffmpeg плеера на хост машине");
            Program.downloadService.StartStream(link);

        }

        private async Task SaveAutoVideo(string link, string channelName)
        {
            Program.downloadService.StartAutoDownload(link, channelName); 
        }

        private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");

            
            if (exception is ApiRequestException apiException)
            {
                Console.WriteLine($"Telegram API Error: {apiException.ErrorCode} - {apiException.Message}");
                if (apiException.ErrorCode == 502) 
                {
                    Console.WriteLine("Попытка переподключения...");
                    await Task.Delay(5000);
                }
            }
            else if (exception is TaskCanceledException)
            {
                Console.WriteLine("Соединение разорвано. Переподключение...");
                await Task.Delay(5000);
            }
        }

        private void StartTrackingChannel()
        {
            if (string.IsNullOrEmpty(_trackedChannel)) return;

            Task.Run(async () =>
            {
                while (true)
                {
                    var link = $"https://twitch.tv/{_trackedChannel}";
                    Console.WriteLine($"Проверка {_trackedChannel} на наличие трансляции");
                    await SaveAutoVideo(link, _trackedChannel);
                    await Task.Delay(TimeSpan.FromSeconds(15)); //2.5 мин блять
                }
            });
        }

        private void LoadTrackedChannel()
        {
            if (File.Exists(_filePath))
            {
                _trackedChannel = File.ReadAllText(_filePath);
            }
        }

        private void SaveTrackedChannel()
        {
            File.WriteAllText(_filePath, _trackedChannel);
        }
    }
}
