using Microsoft.Extensions.Logging;
using DelegationStation.IdentityImporter;


public class Program
{

    static void Main(string[] args)
    {


        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddApplicationInsightsWebJobs(o => { o.ConnectionString = Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING"); });
        });
        ILogger<IdentityImporter> logger = loggerFactory.CreateLogger<IdentityImporter>();


        var importer = new IdentityImporter(loggerFactory);
        importer.RunAsync().Wait();

    }

}
