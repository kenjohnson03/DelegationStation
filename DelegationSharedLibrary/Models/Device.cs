using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace DelegationStationShared.Models
{
    public class Device
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }

        [RegularExpression(@"^[a-zA-z0-9\-]*", ErrorMessage = "Only use letters, numbers, or hyphen for Preferred Host Name value.")]
        [StringLength(63, ErrorMessage = "Preferred Host Name must be less than 63 characters.")]
        public string PreferredHostName { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Make is Required")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.&\(\)\s]+$", ErrorMessage = "Only use letters, numbers, -, _, &, (, ) or . for Make value.")]
        public string Make { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Model is Required")]

        [RegularExpression(@"^[a-zA-Z0-9\-_.&\(\)+\s]+$", ErrorMessage = "Use letters, numbers, -, _, &, (, ), + or . for Model value.")]
        public string Model { get; set; }


        [Required(AllowEmptyStrings = false, ErrorMessage = "Serial Number is Required")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.\s]+$", ErrorMessage = "Use letters, numbers, -, _, or . for SerialNumber value.")]
        public string SerialNumber { get; set; }

        public string PartitionKey { get; set; }

        // More than one tag is not currently supported
        public List<string> Tags { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Type { get; private set; }
        
        // Note:  Because we don't allow modifications from GUI is really AddedUTC
        public DateTime ModifiedUTC { get; set; }

        public string? AddedBy { get; set; }

        // Corporate Identifier related 
        public string CorporateIdentity { get; set; }
        public string CorporateIdentityType { get; set; }
        public DateTime LastCorpIdentitySync { get; set; }
        public string CorporateIdentityID { get; set; }

        public enum DeviceStatus
        {
            Added,
            Synced,
            Deleting
        }
        public DeviceStatus Status { get; set; }
        public DateTime? MarkedToDeleteUTC { get; set; }


        //NOTE:  The following settings are currently unused
        [RegularExpression(@"^([a-fA-F0-9]{2}[:-]){5}([a-fA-F0-9]{2})$", ErrorMessage = "MAC address must use : or -  and be 12 numbers or letters A - F to match the IEEE 802 format")]
        public string MacAddress { get; set; }
        public bool Update { get; set; }
        public List<DeviceUpdateAction> UpdateActions { get; set; }


        public Device()
        {
            Id = Guid.NewGuid();
            PartitionKey = this.Id.ToString();
            PreferredHostName = string.Empty;
            Make = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            MacAddress = string.Empty;
            Tags = new List<string>();
            UpdateActions = new List<DeviceUpdateAction>();
            Type = typeof(Device).Name;
            ModifiedUTC = DateTime.UtcNow;
            MarkedToDeleteUTC = null;

            CorporateIdentity = string.Empty;
            CorporateIdentityType = "manufacturerModelSerial";
            LastCorpIdentitySync = DateTime.MinValue;
            CorporateIdentityID = string.Empty;

            Status = DeviceStatus.Added;


        }
        //public Device(string make, string model, string serialNumber, string macAddress, List<string> tags)
        //{
        //    Id = Guid.NewGuid();
        //    PartitionKey = this.Id.ToString();
        //    Make = make;
        //    Model = model;
        //    SerialNumber = serialNumber;
        //    MacAddress = macAddress;
        //    Tags = tags;
        //    UpdateActions = new List<DeviceUpdateAction>();
        //    Type = typeof(Device).Name;
        //    ModifiedUTC = DateTime.UtcNow;
        //    MarkedToDeleteUTC = null;

        //    CorporateIdentity = string.Empty;
        //    CorporateIdentityType = "manufacturerModelSerial";
        //    LastCorpIdentitySync = DateTime.MinValue;
        //    CorporateIdentityID = string.Empty;

        //    Status = DeviceStatus.Added;

        //}


    }

}
