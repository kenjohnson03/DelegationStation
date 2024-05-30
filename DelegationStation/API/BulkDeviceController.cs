using Azure;
using CsvHelper;
using DelegationStation.Models;
using DelegationStation.Services;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;

namespace DelegationStation.API
{
    [ApiController]
    [Route("api")]
    [Authorize]
    public class BulkDeviceController : Controller
    {
        private readonly ILogger _logger;
        private IDeviceDBService _deviceDBService;
        private IDeviceTagDBService _deviceTagDBService;
        private readonly IConfiguration _config;
        private readonly IAuthorizationService _authorizationService;
        public BulkDeviceController(IDeviceDBService deviceService, IDeviceTagDBService deviceTagDBService, IConfiguration config, ILogger<BulkDeviceController> logger, IAuthorizationService authorizationService) 
        {
            _logger = logger;
            _deviceDBService = deviceService;
            _deviceTagDBService = deviceTagDBService;
            _config = config;
            _authorizationService = authorizationService;
        }

        [HttpGet("BulkDevice")]
        public async Task<IActionResult> Download(string id = "")
        {
            List<string> groups = new List<string>();
            var roleClaims = User.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
            roleClaims = roleClaims ?? new List<System.Security.Claims.Claim>();
            foreach (var c in roleClaims)
            {
                groups.Add(c.Value);
            }
            Role userRole = new Role();
            string defaultGroup = _config.GetSection("DefaultAdminGroupObjectId").Value ?? "";

            if(string.IsNullOrEmpty(id))
            {
                return BadRequest("Tag Id Empty");
            }

            DeviceTag? tag = null;
            try
            {
                tag = await _deviceTagDBService.GetDeviceTagAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError($"BulkDeviceController Download error getting tag {id}.\nError: {ex.Message}");
                return BadRequest("Unable to find tag");
            }
            
            if (tag == null)
            {
                return BadRequest("Unable to find tag");
            }

            if(_authorizationService.AuthorizeAsync(User, tag, Authorization.DeviceTagOperations.Read).Result.Succeeded == false)
            {
                return new UnauthorizedResult();
            }

            List<Device> devices = await _deviceDBService.GetDevicesByTagAsync(id);

            List<BulkDeviceDownloadEntry> entries = new List<BulkDeviceDownloadEntry>();

            foreach (Device device in devices)
            {
               BulkDeviceDownloadEntry entry = new() {
                 Make = device.Make,
                 Model = device.Model,
                 SerialNumber = device.SerialNumber,
                 Action = "",
                 AddedBy = device.AddedBy
               };
               entries.Add(entry);
            }
 
            var stream = new MemoryStream();
            using (var writer = new StreamWriter(stream))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteHeader<BulkDeviceDownloadEntry>();
                csv.NextRecord();

                csv.WriteRecords(entries);
            }
            var content = stream.ToArray();

            string fileName = "Devices.csv";
            return File(content, "text/csv", fileName);
        }
    }
}
