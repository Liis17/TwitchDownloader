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
        public static ReplyKeyboardMarkup GetDownloadKeyboard(string placeholder = "Используй кнопки ниже")
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

        public static ReplyKeyboardMarkup GetDynamicKeyboard(IEnumerable<string> items, string placeholder = "Введи имя канала тут или выбери ниже")
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
        public static ReplyKeyboardMarkup GetSettingsKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "📂 Папка загрузки", "💾 Сохранить настройки", "[placeholder]" },
                new KeyboardButton[] { "[placeholder]", "[placeholder]", "[placeholder]" },
                new KeyboardButton[] { "[placeholder]", "[placeholder]" },
                new KeyboardButton[] { "🏠 Вернуться на главную" }
            })
            {
                InputFieldPlaceholder = "Выбери нужный раздел ниже",
                IsPersistent = true,
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
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
