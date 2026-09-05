namespace DelegationStation.Services
{
    public interface IMaintenanceBannerService
    {
        /// <summary>
        /// Returns the current maintenance banner message, or null if
        /// wwwroot/maintenance-banner.txt does not exist. The file is read on
        /// every call so an admin can create, edit, or remove it without
        /// restarting the app.
        /// </summary>
        string? GetMessage();
    }

    /// <summary>
    /// Displays a site-wide banner sourced from wwwroot/maintenance-banner.txt.
    /// The banner is only shown while that file is present, so operators can
    /// announce maintenance windows or other notices by adding the file and
    /// clear them by deleting it - no deployment or restart required.
    /// </summary>
    public class MaintenanceBannerService : IMaintenanceBannerService
    {
        private const string BannerFileName = "maintenance-banner.txt";

        private readonly string _path;
        private readonly ILogger<MaintenanceBannerService> _logger;

        public MaintenanceBannerService(IWebHostEnvironment env, ILogger<MaintenanceBannerService> logger)
        {
            _path = Path.Combine(env.WebRootPath, BannerFileName);
            _logger = logger;
        }

        public string? GetMessage()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                var message = File.ReadAllText(_path).Trim();
                return string.IsNullOrEmpty(message) ? null : message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read maintenance banner from {Path}.", _path);
                return null;
            }
        }
    }
}
