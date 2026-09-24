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
        int ExpireProcessedDevicesDays { get; set; } = 180;
        int ExpireUnProcessedDevicesDays { get; set; } = 180;


        public override string ToString()
        {
            string output = $"Delegation Station System Settings:\n " +
                $"MaxCorporateIdentifiers: {MaxCorporateIdentifiers}\n " +
                $"ExpireProcessedDevicesDays: {ExpireProcessedDevicesDays}\n " +
                $"ExpireUnProcessedDevicesDays: {ExpireUnProcessedDevicesDays}";
            return output;
        }
    }
}
