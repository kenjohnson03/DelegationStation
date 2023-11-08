using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace DelegationStationShared.Models
{
    public class DeviceBulk
    {
        [Required(AllowEmptyStrings = false)]
        [RegularExpression(@"^[a-zA-Z0-9\-_.\s]+$", ErrorMessage = "Use letters, numbers, -, _, or . for Make value.")]
        public string Make { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Use letters, numbers, -, _, or . for Model value.")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.\s]+$")]

        public string Model { get; set; }


        [Required(AllowEmptyStrings = false)]
        [RegularExpression(@"^[a-zA-Z0-9\-_.\s]+$", ErrorMessage = "Use letters, numbers, -, _, or . for SerialNumber value.")]
        public string SerialNumber { get; set; }

        public DeviceBulkAction Action { get; set; }

        public DeviceBulk()
        {
            Make = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            Action = DeviceBulkAction.add;
        }
    }    

    public enum DeviceBulkAction
    {
        add,
        remove
    }
}
