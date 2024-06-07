
using DelegationStation.WhiteSpaceCleaner;
using Microsoft.Extensions.Logging;

public class Program
{

  static void Main(string[] args)
  {

    
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
      builder.AddConsole();
    });
    ILogger<WhiteSpaceCleaner> logger = loggerFactory.CreateLogger<WhiteSpaceCleaner>();

    var cleaner = new WhiteSpaceCleaner(loggerFactory);
    cleaner.Run();

  }

}
