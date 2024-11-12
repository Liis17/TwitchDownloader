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

            botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Запущен бот {me.Username}");
            StartTrackingChannel(); // Запуск задачи отслеживания канала

            Console.ReadLine();
            cts.Cancel();
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
                        await ShowMainMenu(botClient, message.Chat.Id, cancellationToken);
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
                        await botClient.SendTextMessageAsync(message.Chat.Id, $"Канал {message.Text} добавлен в отслеживаемые.", cancellationToken: cancellationToken);
                        await ShowMainMenu(botClient, message.Chat.Id, cancellationToken);
                    }
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        "Управление доступно только администратору.",
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
                            var buttons = new[]
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
                                    InlineKeyboardButton.WithCallbackData("📝 ffmpeg с задержкой записи", "ffmpegwallclock")
                                },
                                new[]
                                {
                                    InlineKeyboardButton.WithCallbackData("Отменить", "cancel"),
                                }
                             };
                            var keyboard = new InlineKeyboardMarkup(buttons);
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Выберите каким способом выполнить загрузку\nСтандартным и подходящим под большинство сценариев загрузки подходимт \"ffmpeg\", но загрузка активной трансляции таким способом может содержать прерывания звуковой дорожки, для загрузки активной трансляции используйте способы ниже.\n \n(их работоспособность не гарантированна так как не тестировалась на длительном сроке использовния) \n\nДля отмены нажми 'Отменить'.", replyMarkup: keyboard, cancellationToken: cancellationToken);
                            break;


                        case "defaultffmpeg":
                            _waitingForLink = true;
                            downloader = "defaultffmpeg";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введи ссылку на видео Twitch. Для отмены нажми 'Отменить'.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "ffmpegrw_timeout":
                            _waitingForLink = true;
                            downloader = "ffmpegrw_timeout";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введи ссылку на видео Twitch. Для отмены нажми 'Отменить'.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "ffmpegbuffer":
                            _waitingForLink = true;
                            downloader = "ffmpegbuffer";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введи ссылку на видео Twitch. Для отмены нажми 'Отменить'.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "ffmpegwallclock":
                            _waitingForLink = true;
                            downloader = "ffmpegwallclock";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введи ссылку на видео Twitch. Для отмены нажми 'Отменить'.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "ytdlp":
                            _waitingForLink = true;
                            downloader = "ytdlp";
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введи ссылку на видео Twitch. Для отмены нажми 'Отменить'.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;


                        case "track_channel":
                            _waitingForChannel = true;
                            var cancelTrackKeyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Отменить", "cancel"));
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введите имя канала Twitch (без ссылки). Для отмены нажмите 'Отменить'.", replyMarkup: cancelTrackKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "cancel":
                            _waitingForLink = false;
                            _waitingForChannel = false;
                            await ShowMainMenu(botClient, callbackQuery.Message.Chat.Id, cancellationToken);
                            break;
                    }
                }
            }
        }

        public async void SendMessage(string text)
        {
            await botClient.SendTextMessageAsync(AdminId, text, cancellationToken: new CancellationToken());
        }

        private async Task ShowMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
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
            await botClient.SendTextMessageAsync(chatId, $"Сейчас {DateTime.Now}{trackingInfo}\n\nДля загрузки видео с Twitch нажми на кнопку ниже", replyMarkup: keyboard, cancellationToken: cancellationToken);
        }

        private async Task SaveVideo(string link)
        {
            SendMessage($"Загрузка с помощью {downloader}, ожидайте...");
            var a = downloader;
            downloader = string.Empty;
            Program.downloadService.StartDownload(link, a);
            
        }

        private async Task SaveAutoVideo(string link, string channelName)
        {
            Program.downloadService.StartAutoDownload(link, "ytdlp", channelName); //заменить на работающий
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
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
                    await Task.Delay(TimeSpan.FromMinutes(5));
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
