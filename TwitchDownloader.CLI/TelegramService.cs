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

namespace TwitchDownloader.CLI
{
    public class TelegramService
    {
        private string BotToken = "123:abcd";
        public long AdminId = 123;

        public TelegramBotClient botClient;
        private bool _waitingForLink = false; // Флаг ожидания ссылки

        public async Task StartBotAsync(string token, string id)
        {
            BotToken = token;
            AdminId = int.Parse(id);

            botClient = new TelegramBotClient(BotToken);

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
                            await SaveLinkToDatabase(message.Text);
                            await botClient.SendTextMessageAsync(message.Chat.Id, "⭐", cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(message.Chat.Id, "Некорректный формат ссылки. Попробуйте снова.", cancellationToken: cancellationToken);
                        }
                        _waitingForLink = false;
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
                    switch (callbackQuery.Data)
                    {
                        case "add_link":
                            _waitingForLink = true;
                            var cancelKeyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Отменить", "cancel"));
                            await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введи ссылку на видео Twitch. Для отмены нажми 'Отменить'.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                            break;
                        case "cancel":
                            _waitingForLink = false;
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
                InlineKeyboardButton.WithCallbackData("⬇️ Скачать", "add_link")
            }
        };

            var keyboard = new InlineKeyboardMarkup(buttons);
            await botClient.SendTextMessageAsync(chatId, $"Сейчас {DateTime.Now}\n\nДля загрузки видео с Twitch нажми на кнопку ниже", replyMarkup: keyboard, cancellationToken: cancellationToken);
        }

        private async Task SaveLinkToDatabase(string link)
        {
            Program.downloadService.StartDownload(link);
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }
    }
}
