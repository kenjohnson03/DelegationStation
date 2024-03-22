using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UpdateDevices
{
    internal class Program
    {
        static void Main(string[] args)
        {            
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .Build();

            host.Run();
        }
    }
}
