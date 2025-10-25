namespace TwitchDownloader2.CLI
{
    public class Program
    {
        private static string _serviceName = "CLI";
        private static DateTime _startTime = DateTime.Now;
        private static ConsoleColor _consoleColor = ConsoleColor.DarkGreen;
        public static TelegramService TelegramServiceInstance { get; private set; }
        public static AppSettings Settings { get; private set; } = AppSettings.Load();
        public static TwitchCheckerService TwitchChecker { get; private set; }
        public static TwitchDownloaderService TwitchDownloader { get; private set; }
        public static void Main(string[] args)
        {
            // Ensure UTF-8 encoding so emojis render correctly in Windows Terminal
            System.Console.OutputEncoding = System.Text.Encoding.UTF8;
            System.Console.InputEncoding = System.Text.Encoding.UTF8;

            ConsoleWriteLine("--- Twitch Downloader 2 ---");
            ConsoleWriteLine("Версия 2.0.0");

            SettingsChecker();

            Settings.DownloadPath = "C:\\Users\\daske\\Desktop\\testvideo";

            ConsoleWriteLine("Запуск Telegram-сервиса...");

            TelegramServiceInstance = new TelegramService(Settings.TelegramToken, Settings.TelegramIdOwner);
            TelegramServiceInstance.Start();

            ConsoleWriteLine("Запуск TwitchDownloader-сервиса...");
            TwitchDownloader = new TwitchDownloaderService(Settings.DownloadPath);

            ConsoleWriteLine("Запуск TwitchChecker-сервиса...");
            TwitchChecker = new TwitchCheckerService();

            Exit();
        }
        private static void Exit()
        {
            bool exit = true;
            while (exit)
            {
                var stopWord = Console.ReadLine();
                if (stopWord == "STOP")
                {
                    exit = false;
                }
                ConsoleWriteLine("Для выхода введи STOP или нажмите Ctrl+C");
            }
            Settings.Save();
            TelegramServiceInstance.Stop();
        }
        private static void SettingsChecker()
        {
            if (string.IsNullOrEmpty(Settings.TelegramToken))
            {
                ConsoleWriteLine("Не найдет токен бота в настройках");
                ConsoleWriteLine("Введите токен бота: ");
                var token = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    Settings.TelegramToken = token;
                }
            }
            if (Settings.TelegramIdOwner == 0)
            {
                ConsoleWriteLine("Не найдет ID администратора");
                ConsoleWriteLine("Введите ID администратора: ");
                var token = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    try
                    {
                        Settings.TelegramIdOwner = long.Parse(token);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWriteLine($"Ошибка при вводе ID администратора: {ex.Message}", Console.ForegroundColor = ConsoleColor.DarkRed);
                    }
                }
            }

            /// Тут будут проверки других настроек

            Settings.Save();

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

        public static string Uptime
        {
            get
            {
                TimeSpan uptime = DateTime.Now - _startTime;
                int totalHours = (int)uptime.TotalHours;
                int minutes = uptime.Minutes;
                return $"{totalHours:D2}:{minutes:D2}";
            }
        }
    }
}
