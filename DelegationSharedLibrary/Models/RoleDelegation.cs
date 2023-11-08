using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;

namespace DelegationStationShared.Models
{
    public class RoleDelegation
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public Role Role { get; set; }
        [RegularExpression("^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$", ErrorMessage ="Security Group Id must be a valid GUID")]
        public string SecurityGroupId { get; set; }  
        public string SecurityGroupName { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string PartitionKey { get; set; }

        public RoleDelegation()
        {
            Id = Guid.NewGuid();
            Role = new Role();
            SecurityGroupId = string.Empty;
            SecurityGroupName = string.Empty;
            PartitionKey = this.Id.ToString();
        }
    }
}
