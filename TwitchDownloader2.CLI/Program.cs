namespace TwitchDownloader2.CLI
{
    public class Program
    {
        private static string _serviceName = "CLI";
        private static DateTime _startTime = DateTime.Now;
        private static ConsoleColor _consoleColor = ConsoleColor.DarkGreen;
        private static readonly ManualResetEventSlim ShutdownEvent = new(false);
        private static readonly HttpClient PlaybackHttpClient = new();
        public static TelegramService TelegramServiceInstance { get; private set; } = null!;
        public static AppSettings Settings { get; private set; } = AppSettings.Load();
        public static TwitchCheckerService TwitchChecker { get; private set; } = null!;
        public static TwitchDownloaderService TwitchDownloader { get; private set; } = null!;
        public static void Main(string[] args)
        {
            // Ensure UTF-8 encoding so emojis render correctly in Windows Terminal
            System.Console.OutputEncoding = System.Text.Encoding.UTF8;
            System.Console.InputEncoding = System.Text.Encoding.UTF8;

            ConsoleWriteLine("--- Twitch Downloader 2 ---");
            ConsoleWriteLine("Версия 2.0.0");

            SettingsChecker();

            ConsoleWriteLine("Запуск Telegram-сервиса...");

            TelegramServiceInstance = new TelegramService(Settings.TelegramToken, Settings.TelegramIdOwner);

            ConsoleWriteLine("Запуск TwitchDownloader-сервиса...");
            TwitchDownloader = new TwitchDownloaderService(
                Settings,
                new TwitchPlaybackClient(PlaybackHttpClient),
                TelegramServiceInstance);

            ConsoleWriteLine("Запуск TwitchChecker-сервиса...");
            TwitchChecker = new TwitchCheckerService(Settings, TwitchDownloader);
            TwitchChecker.Start();

            TelegramServiceInstance.Start();

            Exit();
        }
        private static void Exit()
        {
            Console.CancelKeyPress += HandleCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += HandleProcessExit;

            try
            {
                if (Console.IsInputRedirected)
                {
                    ConsoleWriteLine("Сервисы запущены. Ожидание сигнала завершения...");
                    ShutdownEvent.Wait();
                }
                else
                {
                    bool exit = false;
                    while (!exit)
                    {
                        var stopWord = Console.ReadLine();
                        if (stopWord is null || string.Equals(stopWord.Trim(), "STOP", StringComparison.OrdinalIgnoreCase))
                        {
                            exit = true;
                        }
                        else
                        {
                            ConsoleWriteLine("Для выхода введи STOP или нажмите Ctrl+C");
                        }
                    }
                }
            }
            finally
            {
                Settings.Save();
                TelegramServiceInstance.Stop();
                TwitchChecker.Dispose();

                Console.CancelKeyPress -= HandleCancelKeyPress;
                AppDomain.CurrentDomain.ProcessExit -= HandleProcessExit;
            }
        }

        private static void HandleCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            ShutdownEvent.Set();
        }

        private static void HandleProcessExit(object? sender, EventArgs args)
        {
            ShutdownEvent.Set();
        }

        private static void SettingsChecker()
        {
            if (Console.IsInputRedirected && !HasRequiredTelegramSettings())
            {
                throw new InvalidOperationException(
                    $"Для запуска без интерактивного ввода задайте {AppSettings.TelegramTokenEnvironmentVariable} и " +
                    $"{AppSettings.TelegramOwnerIdEnvironmentVariable} через переменные окружения.");
            }

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
                var ownerIdText = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(ownerIdText))
                {
                    if (long.TryParse(ownerIdText, out var ownerId) && ownerId > 0)
                    {
                        Settings.TelegramIdOwner = ownerId;
                    }
                    else
                    {
                        ConsoleWriteLine("ID администратора должен быть положительным числом", ConsoleColor.DarkRed);
                    }
                }
            }

            /// Тут будут проверки других настроек

            if (!HasRequiredTelegramSettings())
            {
                throw new InvalidOperationException("Не удалось получить обязательные настройки Telegram-бота.");
            }

            Settings.Save();

        }

        private static bool HasRequiredTelegramSettings()
        {
            return !string.IsNullOrWhiteSpace(Settings.TelegramToken) && Settings.TelegramIdOwner > 0;
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
