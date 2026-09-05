namespace DelegationStation.Services
{
    /// <summary>
    /// The parsed contents of the maintenance banner file: the message to
    /// display and an optional CSS color used to draw attention to it.
    /// </summary>
    public record MaintenanceBannerContent(string Message, string? Color);

    public interface IMaintenanceBannerService
    {
        /// <summary>
        /// Returns the current maintenance banner, or null if
        /// wwwroot/maintenance-banner.txt does not exist or has no message.
        /// The file is read on every call so an admin can create, edit, or
        /// remove it without restarting the app.
        /// </summary>
        MaintenanceBannerContent? GetBanner();
    }

    /// <summary>
    /// Displays a site-wide banner sourced from wwwroot/maintenance-banner.txt.
    /// The banner is only shown while that file is present, so operators can
    /// announce maintenance windows or other notices by adding the file and
    /// clear them by deleting it - no deployment or restart required.
    ///
    /// The file's first line may optionally be a CSS color (e.g. "#ff0000" or
    /// "red" or "rgb(255,0,0)") to draw attention to the banner; the remaining
    /// lines are the message. If the first line is not recognized as a color,
    /// the entire file is treated as the message and the default color is used.
    /// </summary>
    public class MaintenanceBannerService : IMaintenanceBannerService
    {
        private const string BannerFileName = "maintenance-banner.txt";

        private static readonly System.Text.RegularExpressions.Regex ColorLineRegex = new(
            @"^(#[0-9a-fA-F]{3}|#[0-9a-fA-F]{6}|[a-zA-Z]+|(rgb|rgba|hsl|hsla)\(.*\))$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly string _path;
        private readonly ILogger<MaintenanceBannerService> _logger;

        public MaintenanceBannerService(IWebHostEnvironment env, ILogger<MaintenanceBannerService> logger)
        {
            _path = Path.Combine(env.WebRootPath, BannerFileName);
            _logger = logger;
        }

        public MaintenanceBannerContent? GetBanner()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                var lines = File.ReadAllLines(_path);
                return Parse(lines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read maintenance banner from {Path}.", _path);
                return null;
            }
        }

        public static MaintenanceBannerContent? Parse(string[] lines)
        {
            string? color = null;
            var messageLines = lines;

            if (lines.Length > 0 && ColorLineRegex.IsMatch(lines[0].Trim()))
            {
                color = lines[0].Trim();
                messageLines = lines.Skip(1).ToArray();
            }

            var message = string.Join("\n", messageLines).Trim();
            return string.IsNullOrEmpty(message) ? null : new MaintenanceBannerContent(message, color);
        }
    }
}

