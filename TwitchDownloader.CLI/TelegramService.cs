using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

using static Telegram.Bot.TelegramBotClient;

using File = System.IO.File;

public class TelegramService
{
    private ITelegramBotClient _botClient;
    private string _adminId;
    private readonly DownloadService _downloadService;
    private readonly List<string> _trackedChannels = new List<string>();
    private readonly string _channelsFilePath;
    private Dictionary<long, string> _pendingActions = new Dictionary<long, string>();

    public TelegramService(DownloadService downloadService)
    {
        _downloadService = downloadService;
        _channelsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tracked_channels.txt");
        LoadTrackedChannels();
    }

    public async Task StartBotAsync(string token, string adminId)
    {
        _adminId = adminId;
        _botClient = new TelegramBotClient(token);

        var me = await _botClient.GetMeAsync();
        Console.WriteLine($"Bot started: @{me.Username}");

        _botClient.StartReceiving(UpdateHandler, ErrorHandler);

        new Thread(MonitorChannels).Start();
    }
    private void LoadTrackedChannels()
    {
        try
        {
            if (File.Exists(_channelsFilePath))
            {
                _trackedChannels.Clear();
                _trackedChannels.AddRange(File.ReadAllLines(_channelsFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line)));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading channels: {ex.Message}");
        }
    }

    private void SaveTrackedChannels()
    {
        try
        {
            File.WriteAllLines(_channelsFilePath, _trackedChannels);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving channels: {ex.Message}");
        }
    }

    private void MonitorChannels()
    {
        while (true)
        {
            try
            {
                foreach (var channel in _trackedChannels.ToList())
                {
                    if (!_downloadService.IsDownloading(channel))
                    {
                        try
                        {
                            var m3u8Url = GetM3u8Url($"https://twitch.tv/{channel}");
                            if (!string.IsNullOrEmpty(m3u8Url))
                            {
                                Console.WriteLine($"[Monitor] Starting download for {channel}");
                                _downloadService.DownloadStream(m3u8Url, channel, true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Monitor] Error for {channel}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Monitor] Global error: {ex.Message}");
            }
            finally
            {
                Thread.Sleep(60000);
            }
        }
    }

    private Task ErrorHandler(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        Console.WriteLine($"Telegram error: {exception.Message}");
        return Task.CompletedTask;
    }

    private async Task HandleTextCommand(Message message)
    {
        // Обработка текстовых команд, не попавших в другие обработчики
        await _botClient.SendTextMessageAsync(message.Chat.Id, "Неизвестная команда");
    }
    private async Task UpdateHandler(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message?.Chat.Id.ToString() != _adminId && update.CallbackQuery?.From.Id.ToString() != _adminId)
                return;

            if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallbackQuery(update.CallbackQuery);
                return;
            }

            var message = update.Message;

            if (_pendingActions.TryGetValue(message.Chat.Id, out var action))
            {
                await HandlePendingAction(message, action);
                return;
            }

            switch (message.Text)
            {
                case "/start":
                    await ShowMainMenu(message.Chat.Id);
                    break;

                default:
                    await HandleTextCommand(message);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update handler error: {ex.Message}");
        }
    }

    private async Task HandleCallbackQuery(CallbackQuery callbackQuery)
    {
        var data = callbackQuery.Data;
        var chatId = callbackQuery.Message.Chat.Id;

        try
        {
            switch (data)
            {
                case "list_channels":
                    await SendChannelList(chatId);
                    break;

                case "add_channel":
                    _pendingActions[chatId] = "add_channel";
                    await _botClient.SendTextMessageAsync(chatId, "Введите название канала:",
                        replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Отмена", "cancel")));
                    break;

                case "remove_channel":
                    await ShowChannelRemovalMenu(chatId);
                    break;

                case "download":
                    await ShowDownloadOptions(chatId);
                    break;

                case "cancel":
                    _pendingActions.Remove(chatId);
                    await ShowMainMenu(chatId);
                    break;

                case "download_live":
                case "download_archive":
                    _pendingActions[chatId] = data;
                    await _botClient.SendTextMessageAsync(chatId, "Отправьте ссылку на стрим:",
                        replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Отмена", "cancel")));
                    break;

                case var s when s.StartsWith("remove:"):
                    var channelToRemove = s.Split(':')[1];
                    _trackedChannels.Remove(channelToRemove);
                    SaveTrackedChannels();
                    await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, $"Канал {channelToRemove} удален");
                    await ShowMainMenu(chatId);
                    break;

                case var s when s.StartsWith("open:"):
                    var path = s.Split(':')[1];
                    Process.Start("explorer.exe", $"/select,\"{path.Replace("__","\\")}\"");
                    await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
                    break;

                case var s when s.StartsWith("convert:"):
                    //var paths = s.Split(':')[0];
                    Program.converterService.ConvertAndMergeAsync("","");
                    break;

                case "ok":
                    await _botClient.DeleteMessageAsync(chatId, callbackQuery.Message.MessageId);
                    await ShowMainMenu(chatId);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Callback handler error: {ex.Message}");
        }
    }

    private async Task HandlePendingAction(Message message, string action)
    {
        _pendingActions.Remove(message.Chat.Id);

        switch (action)
        {
            case "add_channel":
                if (!_trackedChannels.Contains(message.Text))
                {
                    _trackedChannels.Add(message.Text);
                    SaveTrackedChannels();
                    await _botClient.SendTextMessageAsync(message.Chat.Id, $"Канал {message.Text} добавлен!");
                }
                await ShowMainMenu(message.Chat.Id);
                break;

            case "download_live":
            case "download_archive":
                var m3u8Url = GetM3u8Url(message.Text);
                if (!string.IsNullOrEmpty(m3u8Url))
                {
                    _downloadService.DownloadStream(
                        m3u8Url,
                        Path.GetFileNameWithoutExtension(message.Text),
                        withAudioOffset: action == "download_live"
                    );
                    await _botClient.SendTextMessageAsync(message.Chat.Id, "Загрузка начата!");
                }
                else
                {
                    await _botClient.SendTextMessageAsync(message.Chat.Id, "Не удалось получить ссылку для скачивания");
                }
                await ShowMainMenu(message.Chat.Id);
                break;
        }
    }

    public async void NotifyDownloadComplete(string channel, string filePath, string guid)
    {
        try
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithCallbackData("📼 Конвертировать", "convert:"),
            });

            await _botClient.SendTextMessageAsync(_adminId,
                $"✅ Скачивание {channel} завершено!\n\n 📁 Путь к скачаным файлам: ```Path\n{filePath.Replace($"{channel}_video_{guid}.mp4", "")}```\nФайлы этой записи:```Files\n{channel}_video_{guid}.mp4\n{channel}_audio1_{guid}.aac\n{channel}_audio2_{guid}.aac```",
                /*replyMarkup: keyboard,*/ parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Notify error: {ex.Message}");
        }
    }

    public async void NotifyDownloadStart(string channel, string filePath, string guid)
    {
        try
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithCallbackData("Хорошо", "ok"),
            });

            await _botClient.SendTextMessageAsync(_adminId,
                $"🔔 На канале {channel} началась трансляция!\n\n" +
                $"⬇️  Скачивание  началось!\n\n 📁 Запись будет сохранена по пути: ```Path\n{filePath.Replace($"{channel}_video_{guid}.mp4", "")}```\nФайлы этой записи:```Files\n{channel}_video_{guid}.mp4\n{channel}_audio1_{guid}.aac\n{channel}_audio2_{guid}.aac```",
                /*replyMarkup: keyboard,*/ parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Notify error: {ex.Message}");
        }
    }

    private string GetM3u8Url(string videoUrl)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = $"-g \"{videoUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Добавляем таймаут ожидания
            if (!process.WaitForExit(15000))
            {
                process.Kill();
                return null;
            }

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                Console.WriteLine($"yt-dlp error: {error}");
                return null;
            }

            return process.StandardOutput.ReadLine()?.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetM3u8Url error: {ex.Message}");
            return null;
        }
    }

    private async Task SendChannelList(long chatId)
    {
        var message = _trackedChannels.Count > 0
            ? "📡 Отслеживаемые каналы:\n" + string.Join("\n", _trackedChannels.Select((c, i) => $"{i + 1}. {c}"))
            : "❌ Нет отслеживаемых каналов";

        await _botClient.SendTextMessageAsync(chatId, message);
        await ShowMainMenu(chatId);
    }

    private async Task ShowChannelRemovalMenu(long chatId)
    {
        var buttons = _trackedChannels
            .Select(c => new[] { InlineKeyboardButton.WithCallbackData(c, $"remove:{c}") })
            .ToList();

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") });

        await _botClient.SendTextMessageAsync(chatId, "Выберите канал для удаления:",
            replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task ShowDownloadOptions(long chatId)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Активный стрим", "download_live") },
            new[] { InlineKeyboardButton.WithCallbackData("Архивный стрим", "download_archive") },
            new[] { InlineKeyboardButton.WithCallbackData("Отмена", "cancel") }
        });

        await _botClient.SendTextMessageAsync(chatId, "Выберите тип загрузки:", replyMarkup: keyboard);
    }

    private async Task ShowMainMenu(long chatId)
    {
        var statusMessage = _trackedChannels.Count > 0
            ? string.Join("\n", _trackedChannels.Select(c => $"{c}: {(_downloadService.IsDownloading(c) ? "⏳ Скачивается" : "🕒 Ожидание")}"))
            : "Нет активных загрузок";

        var message = $"📺 Twitch Downloader Bot\n\nСтатус загрузок:\n{statusMessage}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📃 Список каналов", "list_channels") },
            new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить канал", "add_channel") },
            new[] { InlineKeyboardButton.WithCallbackData("➖ Удалить канал", "remove_channel") },
            new[] { InlineKeyboardButton.WithCallbackData("⏬ Скачать сейчас", "download") }
        });

        await _botClient.SendTextMessageAsync(chatId, message, replyMarkup: keyboard);
    }
}