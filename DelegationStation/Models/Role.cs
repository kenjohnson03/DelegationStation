using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStation.Models
{
    public class Role
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public List<DeviceAttribute> Attributes { get; set; } = new List<DeviceAttribute>();
        public bool SecurityGroups { get; set; } = false;
    }
}
