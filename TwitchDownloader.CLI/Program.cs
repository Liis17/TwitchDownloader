using System.Diagnostics;

#pragma warning disable CS8618 

namespace TwitchDownloader.CLI
{
    class Program
    {
        public static TelegramService telegramService;
        public static DownloadService downloadService;
        static void Main(string[] args)
        {
            downloadService = new DownloadService();

            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;

            string tokenPath = Path.Combine(appDirectory, "token");
            string idPath = Path.Combine(appDirectory, "id");

            string token = File.ReadAllText(tokenPath);
            string id = File.ReadAllText(idPath);
            telegramService = new TelegramService();

            telegramService.StartBotAsync(token, id);

            
            while (true) 
            { 
                Console.ReadLine();
                Console.WriteLine("bruh");
            }
           
        }
    }
}
