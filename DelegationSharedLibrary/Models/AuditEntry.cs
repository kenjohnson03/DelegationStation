using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStationShared.Models
{
    public class AuditEntry
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string SourceSystem { get; set; }
        public DateTime TimeGenerated { get; set; }
        public AuditCategory Category { get; set; }
        public string Description { get; set; }
        public Guid CorrelationId { get; set; }
        public Guid Identity { get; set; }
        public string Properties { get; set; }

        [Required(AllowEmptyStrings = false)]
        public string PartitionKey { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Type { get; private set; }

        public AuditEntry()
        {
            Id = Guid.NewGuid();
            TimeGenerated = DateTime.UtcNow;
            TenantId = Guid.Empty;
            SourceSystem = string.Empty;
            Category = AuditCategory.None;
            Description = string.Empty;
            CorrelationId = Guid.Empty;
            Identity = Guid.Empty;
            Properties = string.Empty;
            PartitionKey = this.Id.ToString();
            Type = typeof(AuditEntry).Name;
        }
    }

    public enum AuditCategory
    {
        None,
        RoleDelegation,
        DeviceTag,
        Device,
        DeviceUpdateAction,
        CosmosDB,
        Graph
    }
}
