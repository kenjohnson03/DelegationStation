
using DelegationStation.WhiteSpaceCleaner;
using Microsoft.Extensions.Logging;
using System.Configuration;

public class Program
{

  static void Main(string[] args)
  {

    
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
      builder.AddConsole();
      builder.AddApplicationInsightsWebJobs(o => { o.ConnectionString = Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING"); });
    });
    ILogger<WhiteSpaceCleaner> logger = loggerFactory.CreateLogger<WhiteSpaceCleaner>();

    var cleaner = new WhiteSpaceCleaner(loggerFactory);
    cleaner.Run();

  }

}
