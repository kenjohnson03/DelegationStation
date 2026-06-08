using DelegationStation.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace DelegationStation.Shared
{
    public partial class NavMenu : IDisposable
    {
        private bool collapseNavMenu = true;
        private bool showUpdatesBadge = false;

        [Inject]
        private ProtectedLocalStorage LocalStorage { get; set; } = default!;

        [Inject]
        private RecentUpdatesNotificationService UpdatesNotification { get; set; } = default!;

        [Inject]
        private ILogger<NavMenu> Logger { get; set; } = default!;

        private string? NavMenuCssClass => collapseNavMenu ? "collapse" : null;

        protected override void OnInitialized()
        {
            UpdatesNotification.OnUpdatesViewed += HandleUpdatesViewed;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    var result = await LocalStorage.GetAsync<string>(RecentUpdatesVersion.RecentUpdatesViewedVersionKey);
                    showUpdatesBadge = !result.Success || result.Value != RecentUpdatesVersion.CurrentVersion;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to read recent updates view status from protected local storage.");
                    showUpdatesBadge = true;
                }
                StateHasChanged();
            }
        }

        private void HandleUpdatesViewed()
        {
            showUpdatesBadge = false;
            InvokeAsync(StateHasChanged);
        }

        private void ToggleNavMenu()
        {
            collapseNavMenu = !collapseNavMenu;
        }

        public void Dispose()
        {
            UpdatesNotification.OnUpdatesViewed -= HandleUpdatesViewed;
        }
    }
}