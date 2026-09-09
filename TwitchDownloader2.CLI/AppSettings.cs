using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchDownloader2.CLI
{
    public class AppSettings
    {
        public const string TelegramTokenEnvironmentVariable = "TELEGRAM_BOT_TOKEN";
        public const string TelegramOwnerIdEnvironmentVariable = "TELEGRAM_OWNER_ID";
        public const string DownloadPathEnvironmentVariable = "DOWNLOAD_PATH";

        [JsonIgnore] private static string _serviceName = "AppSettings";
        [JsonIgnore] private static ConsoleColor _consoleColor = ConsoleColor.DarkGreen;


        // ==== Поля настроек ====
        public string TelegramToken { get; set; } = "";
        public long TelegramIdOwner { get; set; } = 0;
        public List<string> TrackedChannels { get; set; } = new List<string>();
        public string DownloadPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");


        // ==== Пути ====
        [JsonIgnore] private static readonly string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        [JsonIgnore] private static readonly string FilePath = Path.Combine(DataDir, "settings.data");

        // ==== Сохранение ====
        public void Save()
        {
            try
            {
                if (!Directory.Exists(DataDir))
                    Directory.CreateDirectory(DataDir);

                // Секреты, переданные через env, не дублируем в settings.data.
                // Остальные настройки (каналы и путь) продолжают сохраняться локально.
                var settingsToSave = new AppSettings
                {
                    TelegramToken = IsEnvironmentVariableDefined(TelegramTokenEnvironmentVariable)
                        ? string.Empty
                        : TelegramToken,
                    TelegramIdOwner = IsEnvironmentVariableDefined(TelegramOwnerIdEnvironmentVariable)
                        ? 0
                        : TelegramIdOwner,
                    TrackedChannels = new List<string>(TrackedChannels ?? new List<string>()),
                    DownloadPath = DownloadPath
                };

                string json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions { WriteIndented = true });
                string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

                File.WriteAllText(FilePath, base64, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка при сохранении настроек: {ex.Message}", ConsoleColor.DarkRed);
            }
        }

        // ==== Загрузка ====
        public static AppSettings Load()
        {
            AppSettings settings;

            try
            {
                if (!File.Exists(FilePath))
                    settings = new AppSettings();
                else
                {
                    string base64 = File.ReadAllText(FilePath, Encoding.UTF8).TrimStart('\uFEFF');
                    string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

                    settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка при загрузке настроек: {ex.Message}", ConsoleColor.DarkRed);
                settings = new AppSettings(); // на случай ошибки — дефолт
            }

            settings.ApplyEnvironmentOverrides();
            return settings;
        }

        private void ApplyEnvironmentOverrides()
        {
            var tokenFromEnvironment = Environment.GetEnvironmentVariable(TelegramTokenEnvironmentVariable);
            if (tokenFromEnvironment is not null)
                TelegramToken = tokenFromEnvironment.Trim();

            var ownerIdFromEnvironment = Environment.GetEnvironmentVariable(TelegramOwnerIdEnvironmentVariable);
            if (ownerIdFromEnvironment is not null)
            {
                if (long.TryParse(ownerIdFromEnvironment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ownerId)
                    && ownerId > 0)
                {
                    TelegramIdOwner = ownerId;
                }
                else
                {
                    TelegramIdOwner = 0;
                    ConsoleWriteLine(
                        $"Переменная {TelegramOwnerIdEnvironmentVariable} должна содержать положительный числовой Telegram ID",
                        ConsoleColor.DarkRed);
                }
            }

            var downloadPathFromEnvironment = Environment.GetEnvironmentVariable(DownloadPathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(downloadPathFromEnvironment))
                DownloadPath = downloadPathFromEnvironment.Trim();
        }

        private static bool IsEnvironmentVariableDefined(string name)
        {
            return Environment.GetEnvironmentVariable(name) is not null;
        }

        private static void ConsoleWriteLine(string message, ConsoleColor color = ConsoleColor.Gray)
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
    }
}
