using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bunit;
using DelegationStation.Pages;
using DelegationStation.Shared;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using DelegationStation.Services;
using Microsoft.Extensions.Logging;
using DelegationStation.Fakes;
using Microsoft.Graph.Models;
using AngleSharp;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using System.Drawing.Text;
using System.Text.RegularExpressions;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class DevicesTest : Bunit.TestContext
    {
        [TestMethod]
        public void DevicesShouldRender()
        {
            using (ShimsContext.Create()) 
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                AddDefaultServices(defaultId.ToString());
                
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                // Act
                var cut = RenderComponent<Devices>();

                // Assert
                string match = @"<td>1<\/td>(.|\n)*<td>2<\/td>(.|\n)*<td>3<\/td>";
                
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void UnauthorizedShouldNotRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                AddDefaultServices();

                var authContext = this.AddTestAuthorization();
                authContext.SetNotAuthorized();                

                // Act
                var cut = RenderComponent<Devices>();

                // Assert
                cut.MarkupMatches(@"
<h3>Devices</h3>

<p>Not Authorized</p>");
            }
        }

        private void AddDefaultServices(string defaultId = "")
        {
            
            //      Create fake services
            List<DeviceTag> deviceTags = new List<DeviceTag>();
            DeviceTag deviceTag1 = new DeviceTag();
            deviceTag1.Name = "testName1";
            deviceTag1.Description = "testDescription1";
            deviceTags.Add(deviceTag1);
            DeviceTag deviceTag2 = new DeviceTag();
            deviceTag2.Name = "testName2";
            deviceTag2.Description = "testDescription2";
            deviceTags.Add(deviceTag2);
            var fakeDeviceTagDBService = new DelegationStation.Services.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagsAsyncIEnumerableOfString =
                    (groupIds) => Task.FromResult(deviceTags)
            };

            DelegationStationShared.Models.Device device1 = new DelegationStationShared.Models.Device()
            {
                Make = "1",
                Model = "2",
                SerialNumber = "3"
            };
            List<DelegationStationShared.Models.Device> devices = new List<DelegationStationShared.Models.Device>();
            devices.Add(device1);
            var fakeDevicDBService = new DelegationStation.Services.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringStringInt32Int32 = (a, b, c, d) =>
                    Task.FromResult(devices)
            };

            var myConfiguration = new Dictionary<string, string>
                {
                    {"DefaultAdminGroupObjectId", defaultId},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(myConfiguration)
                .Build();


            //      Add Dependent Services
            Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            Services.AddSingleton<IDeviceDBService>(fakeDevicDBService);
            Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        }
    }
}