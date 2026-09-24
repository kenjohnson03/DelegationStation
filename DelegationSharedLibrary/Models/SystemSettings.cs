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
        int MaxCorporateIdentifiers { get; set; } = 10000;
        int InactiveProcessedDevicesDays { get; set; } = 180;
        int InactiveUnProcessedDevicesDays { get; set; } = 180;


        public override string ToString()
        {
            string output = $"Delegation Station System Settings:\n " +
                $"MaxCorporateIdentifiers: {MaxCorporateIdentifiers}\n " +
                $"InactiveProcessedDevicesDays: {InactiveProcessedDevicesDays}\n " +
                $"InactiveUnProcessedDevicesDays: {InactiveUnProcessedDevicesDays}";
            return output;
        }
    }
}
