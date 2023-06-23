using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace DelegationStationShared.Models
{
    public class Device
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Make { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Model { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string SerialNumber { get; set; }

        public string MacAddress { get; set; }
        public string PartitionKey { get; set; }

        public List<string> Tags { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Type { get; private set; }

        public DateTime ModifiedUTC { get; set; }
        public List<DeviceUpdateAction> UpdateActions { get; set; }

        public Device()
        {
            Id = Guid.NewGuid();
            PartitionKey = this.Id.ToString();
            Make = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            MacAddress = string.Empty;
            Tags = new List<string>();
            UpdateActions = new List<DeviceUpdateAction>();
            Type = typeof(Device).Name;
            ModifiedUTC = DateTime.UtcNow;
        }        
    }
}
