using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStationShared.Models
{
    public class DeviceTag
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<RoleDelegation> RoleDelegations { get; set; }
        public List<DeviceUpdateAction> UpdateActions { get; set; }

        [Required(AllowEmptyStrings = false)]
        public string PartitionKey { get; set; }

        public DeviceTag () 
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            Description = string.Empty;
            RoleDelegations = new List<RoleDelegation>();
            UpdateActions = new List<DeviceUpdateAction>();
            PartitionKey = typeof(DeviceTag).Name;
        }

    }
}
