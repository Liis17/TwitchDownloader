using System.Diagnostics;

#pragma warning disable CS8618 

namespace TwitchDownloader.CLI
{
    class Program
    {
        public static TelegramService TelegaSrv; //хуега блять
        public static DownloadService downloadService;
        public static string path = string.Empty;


        static void Main(string[] args)
        {
            var _path = string.Empty;
            if (args.Length != 0)
            {
                _path = args[0];
                if (!Directory.Exists(_path))
                {
                    Console.WriteLine("Указанный аргумент запуска не являектся существующим путем на диске!");
                    Console.ReadKey();
                    return;
                }
                else
                {
                    Console.WriteLine($"Путь для загрузки: {_path}");
                    path = _path;
                }
            }
            
            downloadService = new DownloadService(path);

            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;

            string tokenPath = Path.Combine(appDirectory, "token"); // по хорошему не так, но тут похуй
            string idPath = Path.Combine(appDirectory, "id");

            string token = File.ReadAllText(tokenPath);
            string id = File.ReadAllText(idPath);
            TelegaSrv = new TelegramService();

            TelegaSrv.StartBotAsync(token, id);

            
            while (true) 
            { 
                Console.ReadLine();
                Console.WriteLine("bruh");
            }
           
        }
    }
}
