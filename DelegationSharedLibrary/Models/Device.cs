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
        public string PartitionKey { get; set; }

        public List<string> Tags { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Type { get; private set; }

        public DateTime ModifiedUTC { get; set; }
        public List<DeviceUpdateAction> UpdateActions { get; set; }

        public Device()
        {
            this.Id = Guid.NewGuid();
            this.PartitionKey = this.Id.ToString();
            this.Make = "";
            this.Model = "";
            this.SerialNumber = "";
            this.Tags = new List<string>();
            this.UpdateActions = new List<DeviceUpdateAction>();
            this.Type = typeof(Device).Name;
            ModifiedUTC = DateTime.UtcNow;
        }        
    }
}
