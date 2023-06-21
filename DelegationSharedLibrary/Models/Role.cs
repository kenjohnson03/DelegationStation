using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStationShared.Models
{
    public class Role
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<AllowedAttributes> Attributes { get; set; }
        public bool SecurityGroups { get; set; }
        public bool AdministrativeUnits { get; set; }
        public string PartitionKey { get; set; }

        public Role () 
        { 
            Id = Guid.NewGuid();
            Name = string.Empty;
            Attributes = new List<AllowedAttributes> ();
            SecurityGroups = false;
            AdministrativeUnits = false;
            PartitionKey = typeof(Role).Name;
        }
    }
}
