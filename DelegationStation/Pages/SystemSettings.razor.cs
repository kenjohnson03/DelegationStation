using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.ComponentModel.DataAnnotations;

namespace DelegationStation.Pages
{
    public partial class SystemSettings
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        [Inject]
        private NavigationManager nav { get; set; } = default!;

        private DelegationStationShared.Models.SystemSettings settings = new DelegationStationShared.Models.SystemSettings();
        private SystemSettingsModel model = new SystemSettingsModel();

        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            model = SystemSettingsModel.FromSettings(settings);
        }

        private void Save()
        {
            model.ApplyTo(settings);
        }

        private void Cancel()
        {
            model = SystemSettingsModel.FromSettings(settings);
        }

        private const string PositiveIntegerPattern = "^[1-9][0-9]*$";
        private const string PositiveIntegerError = "Enter a positive whole number.";

        private class SystemSettingsModel
        {
            [Required(ErrorMessage = PositiveIntegerError)]
            [RegularExpression(PositiveIntegerPattern, ErrorMessage = PositiveIntegerError)]
            public string MaxCorpIdsAllowed { get; set; } = string.Empty;

            [Required(ErrorMessage = PositiveIntegerError)]
            [RegularExpression(PositiveIntegerPattern, ErrorMessage = PositiveIntegerError)]
            public string CorpIdWarningThresholdPercent { get; set; } = string.Empty;

            [Required(ErrorMessage = PositiveIntegerError)]
            [RegularExpression(PositiveIntegerPattern, ErrorMessage = PositiveIntegerError)]
            public string ExpireProcessedAfterDays { get; set; } = string.Empty;

            [Required(ErrorMessage = PositiveIntegerError)]
            [RegularExpression(PositiveIntegerPattern, ErrorMessage = PositiveIntegerError)]
            public string ExpireUnprocessedAfterDays { get; set; } = string.Empty;

            public static SystemSettingsModel FromSettings(DelegationStationShared.Models.SystemSettings settings)
            {
                return new SystemSettingsModel
                {
                    MaxCorpIdsAllowed = ToFieldValue(settings.MaxCorpIdsAllowed),
                    CorpIdWarningThresholdPercent = ToFieldValue(settings.CorpIdWarningThresholdPercent),
                    ExpireProcessedAfterDays = ToFieldValue(settings.ExpireProcessedAfterDays),
                    ExpireUnprocessedAfterDays = ToFieldValue(settings.ExpireUnprocessedAfterDays)
                };
            }

            public void ApplyTo(DelegationStationShared.Models.SystemSettings settings)
            {
                settings.MaxCorpIdsAllowed = int.Parse(MaxCorpIdsAllowed);
                settings.CorpIdWarningThresholdPercent = int.Parse(CorpIdWarningThresholdPercent);
                settings.ExpireProcessedAfterDays = int.Parse(ExpireProcessedAfterDays);
                settings.ExpireUnprocessedAfterDays = int.Parse(ExpireUnprocessedAfterDays);
                settings.ModifiedDT = DateTime.UtcNow;
            }

            private static string ToFieldValue(int value)
            {
                return value > 0 ? value.ToString() : string.Empty;
            }
        }
    }
}


