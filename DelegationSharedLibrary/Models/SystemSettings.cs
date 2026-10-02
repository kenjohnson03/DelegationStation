using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationSharedLibrary.Models
{
    public class SystemSettings
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = "system-settings";
        public string PartitionKey { get; set; } = "SystemSettings";

        // System Settings Values
        public int MaxCorporateIdentifiers { get; set; } = 10000;
        public const int DefaultProcessedDevicesExpiredAfterDays = 180;
        public int ProcessedDevicesExpiredAfterDays { get; set; } = DefaultProcessedDevicesExpiredAfterDays;
        public const int DefaultUnprocessedDevicesExpiredAfterDays = 180;
        public int UnprocessedDevicesExpiredAfterDays { get; set; } = DefaultUnprocessedDevicesExpiredAfterDays;


        public override string ToString()
        {
            string output = $"Delegation Station System Settings:\n " +
                $"MaxCorporateIdentifiers: {MaxCorporateIdentifiers}\n " +
                $"ProcessedDevicesExpiredAfterDays: {ProcessedDevicesExpiredAfterDays}\n " +
                $"UnprocessedDevicesExpiredAfterDays: {UnprocessedDevicesExpiredAfterDays}";
            return output;
        }
    }
}
