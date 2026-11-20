using Telegram.Bot.Types.ReplyMarkups;

namespace TwitchDownloader2.CLI
{
    public class Keyboards
    {
        public static InlineKeyboardMarkup GetEditPathButton()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🖊️ Изменить", "editdownloadpath") }
            });
        }

        public static ReplyKeyboardMarkup GetMainKeyboard(string placeholder = "Используй кнопки ниже")
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "📺 Каналы", "📜 Статус", "🏺 История" },
                new KeyboardButton[] { "⬇️ Загрузить", "⛔ Остановить запись" },
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
        public static ReplyKeyboardMarkup GetOnlyCancelKeyboard(string placeholder = "Введи это сюда")
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
        public static ReplyKeyboardMarkup GetServiceKeyboard(string placeholder = "Введи это сюда")
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "❌ Отменить действие" },
                new KeyboardButton[] { "📺 Каналы", "📜 Статус", "🏺 История" },
                new KeyboardButton[] { "⬇️ Загрузить", "⛔ Остановить запись" },
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

        public static ReplyKeyboardMarkup GetSettingsKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "📂 Папка загрузки", "💾 Сохранить настройки" },
                new KeyboardButton[] { "🏠 Вернуться на главную" }
            })
            {
                InputFieldPlaceholder = "Выбери нужный раздел ниже",
                IsPersistent = true,
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }

        public static ReplyKeyboardMarkup GetChannelsKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "➕ Добавить", "🗑️ Удалить" },
                new KeyboardButton[] { "🏠 Вернуться на главную" }
            })
            {
                InputFieldPlaceholder = "Управление каналами",
                IsPersistent = true,
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }

        public static InlineKeyboardMarkup GetDeleteChannelsKeyboard(IEnumerable<string> channels)
        {
            var rows = channels.Select(channel => new[]
            {
                InlineKeyboardButton.WithCallbackData($"🗑️ {channel}", CreateDeleteChannelCallback(channel))
            });
            return new InlineKeyboardMarkup(rows);
        }

        internal static string CreateDeleteChannelCallback(string channel)
        {
            return $"{DeleteChannelCallbackPrefix}{channel}";
        }

        internal static bool TryParseDeleteChannelCallback(string? callbackData, out string channel)
        {
            if (callbackData is not null
                && callbackData.StartsWith(DeleteChannelCallbackPrefix, StringComparison.Ordinal)
                && callbackData.Length > DeleteChannelCallbackPrefix.Length)
            {
                channel = callbackData[DeleteChannelCallbackPrefix.Length..];
                return true;
            }

            channel = string.Empty;
            return false;
        }

        private const string DeleteChannelCallbackPrefix = "delete_channel:";

        public static InlineKeyboardMarkup GetStopDownloadsKeyboard(IEnumerable<ActiveDownloadInfo> downloads)
        {
            var rows = downloads.Select(download => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    download.Kind == RecordingKind.Vod
                        ? $"⛔ VOD {download.VodId} · {download.Channel} · {download.State}"
                        : $"⛔ {download.Channel} · {download.State}",
                    CreateStopDownloadCallback(download.SessionId))
            });
            return new InlineKeyboardMarkup(rows);
        }

        internal static string CreateStopDownloadCallback(string sessionId)
        {
            return $"stop_download:{sessionId}";
        }

        internal static bool TryParseStopDownloadCallback(string? callbackData, out string sessionId)
        {
            const string prefix = "stop_download:";
            if (callbackData is not null
                && callbackData.StartsWith(prefix, StringComparison.Ordinal)
                && callbackData.Length > prefix.Length)
            {
                sessionId = callbackData[prefix.Length..];
                return true;
            }

            sessionId = string.Empty;
            return false;
        }


        public static ReplyKeyboardMarkup GetPathEditKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "🏠 Вернуться на главную" },
            })
            {
                InputFieldPlaceholder = "Хочешь изменить путь?",
                IsPersistent = true,
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }
    }
}
