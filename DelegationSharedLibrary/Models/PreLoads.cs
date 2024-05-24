using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationSharedLibrary.Models
{
  public class PreLoads
  {
    [Required]
    [JsonProperty(PropertyName = "id")]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string[] Values { get; set; }
  }
}
