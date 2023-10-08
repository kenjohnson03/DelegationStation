using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntuneEnrollment.Models
{
    public class FunctionSettings
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public string PartitionKey { get; set; }
        public DateTime? LastRun { get; set; }

        public FunctionSettings()
        {
            Id = Guid.Parse("f990517b-3927-4429-82b0-712d4856110e");
            PartitionKey = "FunctionSettings";
            LastRun = null;
        }
    }
}
