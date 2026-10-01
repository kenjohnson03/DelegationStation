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
        public int InactiveProcessedDevicesDays { get; set; } = 180;
        public int InactiveUnprocessedDevicesDays { get; set; } = 180;


        public override string ToString()
        {
            string output = $"Delegation Station System Settings:\n " +
                $"MaxCorporateIdentifiers: {MaxCorporateIdentifiers}\n " +
                $"InactiveProcessedDevicesDays: {InactiveProcessedDevicesDays}\n " +
                $"InactiveUnprocessedDevicesDays: {InactiveUnprocessedDevicesDays}";
            return output;
        }
    }
}
