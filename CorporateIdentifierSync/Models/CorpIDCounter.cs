using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace CorporateIdentifierSync.Models
{
    public class CorpIDCounter
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid id { get; set; }
        public string PartitionKey { get; set; }
        public int CorpIDCount { get; set; }
        public int CorpIDReserve { get; set; }

        public CorpIDCounter() {
            id = Guid.NewGuid();
            PartitionKey = "CorpIDCounter";
            CorpIDCount = 0;
            CorpIDReserve = 0;
        }

        public override string ToString()
        {
            string output = $"CorpIDCount: {CorpIDCount}, CorpIDReserve: {CorpIDReserve}";
            return output;
        }

        public int GetTotal()
        {
            return CorpIDCount + CorpIDReserve;
        }
    }

   }
