using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateDevices.Interfaces;
using UpdateDevices.Services;

namespace UpdateDevices
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var builder = FunctionsApplication.CreateBuilder(args);

            builder.ConfigureFunctionsWebApplication();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);

            builder.Services.AddApplicationInsightsTelemetryWorkerService();
            builder.Services.ConfigureFunctionsApplicationInsights();
            builder.Services.AddSingleton<ICosmosDbService, CosmosDbService>();
            builder.Services.AddSingleton<IGraphBetaService, GraphBetaService>();
            builder.Services.AddSingleton<IGraphService, GraphService>();

            var host = builder.Build();
            host.Run();
        }
    }
}
