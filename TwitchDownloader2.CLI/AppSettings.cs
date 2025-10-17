using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchDownloader2.CLI
{
    public class AppSettings
    {
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

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

                File.WriteAllText(FilePath, base64, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка при сохранении настроек: {ex.Message}", ConsoleColor.DarkRed);
            }
        }

        // ==== Загрузка ====
        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new AppSettings();

                string base64 = File.ReadAllText(FilePath, Encoding.UTF8);
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                ConsoleWriteLine($"Ошибка при загрузке настроек: {ex.Message}", ConsoleColor.DarkRed);
                return new AppSettings(); // на случай ошибки — дефолт
            }
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
