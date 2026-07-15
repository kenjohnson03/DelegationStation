using DelegationStationShared.Models;
using Microsoft.AspNetCore.Components;

namespace DelegationStation.Pages
{
    public partial class SystemStatus
    {
        private const int DefaultMaxCorpIDsAllowed = 10000;

        private bool loading = true;
        private string errorMessage = string.Empty;

        private int MaxCorpIDsAllowed { get; set; } = DefaultMaxCorpIDsAllowed;
        private int CorpIDCount { get; set; } = 0;

        // If the count exceeds the limit, display it as being at the limit.
        private int DisplayCount => Math.Min(CorpIDCount, MaxCorpIDsAllowed);

        // Percentage of the max being utilized, capped at 100%.
        private double DisplayPercentage
        {
            get
            {
                if (MaxCorpIDsAllowed <= 0)
                {
                    return 0;
                }

                double percentage = (double)CorpIDCount / MaxCorpIDsAllowed * 100;
                return Math.Min(percentage, 100);
            }
        }

        // RED at or above 100%, Yellow above 90%, Green otherwise.
        private string UtilizationCssClass
        {
            get
            {
                if (MaxCorpIDsAllowed <= 0)
                {
                    return "text-success fw-bold";
                }

                double actualPercentage = (double)CorpIDCount / MaxCorpIDsAllowed * 100;
                if (actualPercentage >= 100)
                {
                    return "text-danger fw-bold";
                }
                if (actualPercentage > 90)
                {
                    return "text-warning fw-bold";
                }
                return "text-success fw-bold";
            }
        }

        protected override async Task OnInitializedAsync()
        {
            try
            {
                MaxCorpIDsAllowed = GetMaxCorpIDsAllowed();

                CorpIDCounter? counter = await deviceTagDBService.GetCorpIDCounterAsync();
                if (counter == null)
                {
                    errorMessage = "Corporate Identifier counter was not found in the database.";
                }
                else
                {
                    CorpIDCount = counter.CorpIDCount;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load Corporate Identifier status.");
                errorMessage = "Failed to load Corporate Identifier status.";
            }
            finally
            {
                loading = false;
            }
        }

        private int GetMaxCorpIDsAllowed()
        {
            string? maxCorpIDsString = Environment.GetEnvironmentVariable("MAX_CORPIDS_ALLOWED");
            if (!int.TryParse(maxCorpIDsString, out int max) || max <= 0)
            {
                logger.LogWarning("MAX_CORPIDS_ALLOWED is not set or invalid. Using default value: {DefaultMax}.", DefaultMaxCorpIDsAllowed);
                return DefaultMaxCorpIDsAllowed;
            }

            return max;
        }
    }
}
