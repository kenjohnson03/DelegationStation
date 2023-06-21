using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStation.Models
{
    public class Role
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public List<AllowedAttributes> Attributes { get; set; } = new List<AllowedAttributes>();
        public bool SecurityGroups { get; set; } = false;
        public bool AdministrativeUnits { get; set; } = false;
        public string PartitionKey { get; set; } = typeof(Role).Name;
    }
}
