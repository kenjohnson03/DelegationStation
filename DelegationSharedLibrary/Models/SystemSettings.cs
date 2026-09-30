using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStationShared.Models
{
    public class SystemSettings
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }

        public string PartitionKey { get; set; }

        public int MaxCorpIdsAllowed { get; set; }

        public int CorpIdWarningThresholdPercent { get; set; }

        public int ExpireProcessedAfterDays { get; set; }

        public int ExpireUnprocessedAfterDays { get; set; }

        public DateTime CreatedDT { get; set; }

        public DateTime ModifiedDT { get; set; }

        public SystemSettings()
        {
            Id = Guid.NewGuid();
            PartitionKey = "SystemSettings";
            CreatedDT = DateTime.UtcNow;
            ModifiedDT = DateTime.UtcNow;
        }

        public override string ToString()
        {
            return $"MaxCorpIdsAllowed: {MaxCorpIdsAllowed}, CorpIdWarningThresholdPercent: {CorpIdWarningThresholdPercent}, " +
                   $"ExpireProcessedAfterDays: {ExpireProcessedAfterDays}, ExpireUnprocessedAfterDays: {ExpireUnprocessedAfterDays}";
        }

        [JsonProperty("_etag")]
        public string ETag { get; set; }
    }
}
