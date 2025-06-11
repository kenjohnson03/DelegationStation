using Microsoft.Extensions.Logging;

namespace RemoveCaseSensitiveDuplicates
{
    public class Program
    {
        static void Main(string[] args)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddApplicationInsightsWebJobs(o => { o.ConnectionString = Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING"); });
            });
            ILogger<RemoveDupes> logger = loggerFactory.CreateLogger<RemoveDupes>();


            var dupeRemover = new RemoveDupes(loggerFactory);
            dupeRemover.RunAsync().Wait();

        }
    }
}
