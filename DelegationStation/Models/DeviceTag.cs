using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStation.Models
{
    public class DeviceTag
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Name { get; set; }
        public string Description { get; set; }
        public List<RoleDelegation> RoleDelegations { get; set; }
        public List<DeviceUpdateAction> UpdateActions { get; set; }
        public int Order { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string PartitionKey { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Type { get; private set; }


        public DeviceTag() 
        {
            this.Id = Guid.NewGuid();
            this.Name = string.Empty;
            this.Description = string.Empty;
            this.RoleDelegations = new List<RoleDelegation>();
            this.UpdateActions = new List<DeviceUpdateAction>();
            this.Order = 0;
            this.PartitionKey = "DeviceTag";
            this.Type = typeof(DeviceTag).Name;
        }
    }
}
