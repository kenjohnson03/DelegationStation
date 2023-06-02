using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStation.Models
{
    public class DeviceAttribute
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; } = default(Guid);
        public string Name { get; set; } = String.Empty;
    }
}
