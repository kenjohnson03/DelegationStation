using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStation.Models
{
    public class DeviceTag
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required(AllowEmptyStrings = false)]
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<RoleDelegation> RoleDelegations { get; set; } = new List<RoleDelegation>();
        public List<DeviceUpdateAction> UpdateActions { get; set; } = new List<DeviceUpdateAction>();

        [Required(AllowEmptyStrings = false)]
        public string PartitionKey { get; set; } = typeof(DeviceTag).Name;
    }
}
