using System;
using System.IO;
using System.Security.Principal;
using System.Threading;

class Program
{
    public static DownloadService downloadService;
    public static TelegramService telegramService;
    public static ConverterService converterService;
    static void Main(string[] args)
    {
        if (!IsAdministrator())
        {
            Console.WriteLine("Требуются права администратора!");
            return;
        }

        string savePath = args.Length > 0 ? args[0] : string.Empty;
        downloadService = new DownloadService(savePath);
        telegramService = new TelegramService(downloadService);
        converterService = new ConverterService();

        string token = File.ReadAllText("token");
        string adminId = File.ReadAllText("id");

        telegramService.StartBotAsync(token, adminId).Wait();

        Thread.Sleep(Timeout.Infinite);
    }

    private static bool IsAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}