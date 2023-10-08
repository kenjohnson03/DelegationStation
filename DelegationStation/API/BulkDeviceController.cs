using DelegationStation.Services;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.Security;
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
        public BulkDeviceController(IDeviceDBService deviceService, IDeviceTagDBService deviceTagDBService, IConfiguration config, ILogger<BulkDeviceController> logger) 
        {
            _logger = logger;
            _deviceDBService = deviceService;
            _deviceTagDBService = deviceTagDBService;
            _config = config;
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

            DeviceTag tag = null;
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

            userRole = userRole.GetRole(groups, defaultGroup, tag);

            if(userRole.IsDefaultRole())
            {
                return new UnauthorizedResult();
            }
            string fileName = "Devices.csv";
            List<Device> devices = await _deviceDBService.GetDevicesByTagAsync(id);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Make,Model,SerialNumber,Action,AddedBy");
            if (!string.IsNullOrEmpty(id))
            {
                foreach (Device device in devices)
                {
                    sb.AppendLine($"{device.Make},{device.Model},{device.SerialNumber},,{device.AddedBy}");
                }
            }            

            byte[] fileBytes = Encoding.ASCII.GetBytes(sb.ToString());

            return File(fileBytes, "text/csv", fileName);
        }
    }
}
