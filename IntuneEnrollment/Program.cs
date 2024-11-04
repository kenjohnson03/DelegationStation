using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;



namespace DelegationStation.Function
{
  internal class Program
  {
    static void Main(string[] args)
    {

      var host = new HostBuilder()
          .ConfigureFunctionsWebApplication()
          .ConfigureLogging(logging =>
          {
            logging.SetMinimumLevel(LogLevel.Debug);
          }).
          //ConfigureServices(services =>
          //{
          //  services.AddSingleton<ICosmosDbService, CosmosDbService>();
          //})
    ConfigureServices(services => {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
          .Build();

      host.Run();
    }

  }
}
