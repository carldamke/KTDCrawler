namespace KTDCrawler
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting the KTDCrawler...");

            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .Build();

            host.Run();
        }
    }
}
