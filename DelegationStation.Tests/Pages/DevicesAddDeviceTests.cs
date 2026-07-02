using DelegationStation.Interfaces;
using DelegationStation.Pages;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;

namespace DelegationStation.Tests.Pages
{
    /// <summary>
    /// Tests that verify the behavior when adding devices, focusing on the non-Windows
    /// serial number uniqueness enforcement. These are page-level tests: they verify that
    /// exceptions thrown by IDeviceDBService are surfaced correctly to the user.
    ///
    /// The actual DB-level uniqueness check (d.OS > 1 query in DeviceDBService) requires
    /// integration tests against a real or emulated Cosmos DB instance.
    /// </summary>
    [TestClass]
    public class DevicesAddDeviceTests : Bunit.TestContext
    {
        private const string DuplicateSerialErrorMessage = "A non-Windows device with this Serial Number already exists.";

        private IRenderedComponent<Devices> SetupComponent(
            List<DeviceTag> deviceTags,
            Func<Device, Task<Device>> addOrUpdateStub,
            string defaultAdminGroupId = "")
        {
            if (string.IsNullOrEmpty(defaultAdminGroupId))
                defaultAdminGroupId = Guid.NewGuid().ToString();

            var authContext = this.AddTestAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(
                new System.Security.Claims.Claim("name", "TEST USER"),
                new System.Security.Claims.Claim(
                    "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
                    defaultAdminGroupId)
            );

            var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagsAsyncIEnumerableOfStringString =
                    (groupIds, name) => Task.FromResult(deviceTags),
                GetDeviceTagCountAsyncIEnumerableOfStringString =
                    (groupIds, name) => Task.FromResult(deviceTags.Count),
                GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                    (groupIds, pageNumber, pageSize, name) => Task.FromResult(deviceTags)
            };

            var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringInt32Int32 =
                    (groupIds, pageSize, currentPage) => Task.FromResult(new List<Device>()),
                AddOrUpdateDeviceAsyncDevice = device => addOrUpdateStub(device)
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "DefaultAdminGroupObjectId", defaultAdminGroupId }
                })
                .Build();

            Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            Services.AddSingleton<IConfiguration>(config);

            return RenderComponent<Devices>();
        }

        /// <summary>
        /// Fills in the add-device form fields and clicks the Add button.
        /// osValue is the integer string of the DeviceOS enum (e.g. "1"=Windows, "2"=MacOS, "3"=iOS, "4"=Android).
        /// </summary>
        private static void FillAndSubmitAddForm(
            IRenderedComponent<Devices> cut,
            string make,
            string model,
            string serialNumber,
            string osValue)
        {
            cut.Find("#DeviceMake").Change(make);
            cut.Find("#DeviceModel").Change(model);
            cut.Find("#SerialNumber").Change(serialNumber);
            cut.Find("#OS").Change(osValue);
            // Select the first available tag via its checkbox
            cut.Find(".form-check-input").Change(true);
            cut.Find("input[value='Add']").Click();
        }

        [TestMethod]
        [DataRow("2", "MacOS")]
        [DataRow("3", "iOS")]
        [DataRow("4", "Android")]
        public void AddDevice_NonWindowsDevice_DuplicateSerial_ShowsErrorMessage(string osValue, string osDisplayName)
        {
            using (ShimsContext.Create())
            {
                // Arrange
                var tag = new DeviceTag { Id = Guid.NewGuid(), Name = "TestTag" };
                var cut = SetupComponent(
                    new List<DeviceTag> { tag },
                    device => Task.FromException<Device>(new Exception(DuplicateSerialErrorMessage))
                );

                // Act
                FillAndSubmitAddForm(cut, "TestMake", "TestModel", "SN12345", osValue);

                // Assert: the duplicate serial error from the DB service is surfaced to the user
                cut.WaitForAssertion(() =>
                    Assert.IsTrue(
                        cut.Markup.Contains(DuplicateSerialErrorMessage),
                        $"Expected duplicate serial error for {osDisplayName} device. Markup: {cut.Markup}"
                    )
                );
            }
        }

        [TestMethod]
        [DataRow("2", "MacOS")]
        [DataRow("3", "iOS")]
        [DataRow("4", "Android")]
        public void AddDevice_NonWindowsDevice_UniqueSerial_ShowsSuccessMessage(string osValue, string osDisplayName)
        {
            using (ShimsContext.Create())
            {
                // Arrange
                var tag = new DeviceTag { Id = Guid.NewGuid(), Name = "TestTag" };
                var addedDevice = new Device { Make = "TestMake", Model = "TestModel", SerialNumber = "SN-UNIQUE" };
                var cut = SetupComponent(
                    new List<DeviceTag> { tag },
                    device => Task.FromResult(addedDevice)
                );

                // Act
                FillAndSubmitAddForm(cut, "TestMake", "TestModel", "SN-UNIQUE", osValue);

                // Assert: a device with a unique serial number is added successfully
                cut.WaitForAssertion(() =>
                    Assert.IsTrue(
                        cut.Markup.Contains("Device added successfully"),
                        $"Expected success message for {osDisplayName} device with unique serial. Markup: {cut.Markup}"
                    )
                );
            }
        }

        [TestMethod]
        public void AddDevice_WindowsDevice_NotBlockedByNonWindowsSerialCheck_ShowsSuccessMessage()
        {
            using (ShimsContext.Create())
            {
                // Arrange: Windows devices are exempt from the non-Windows serial uniqueness check.
                // The stub returns success unconditionally; no duplicate-serial exception is thrown.
                var tag = new DeviceTag { Id = Guid.NewGuid(), Name = "TestTag" };
                var addedDevice = new Device { Make = "Dell", Model = "Latitude", SerialNumber = "SN12345" };
                var cut = SetupComponent(
                    new List<DeviceTag> { tag },
                    device => Task.FromResult(addedDevice)
                );

                // Act - "1" = DeviceOS.Windows
                FillAndSubmitAddForm(cut, "Dell", "Latitude", "SN12345", "1");

                // Assert
                cut.WaitForAssertion(() =>
                    Assert.IsTrue(
                        cut.Markup.Contains("Device added successfully"),
                        $"Windows device should be added successfully. Markup: {cut.Markup}"
                    )
                );
            }
        }

        [TestMethod]
        public void AddDevice_ServiceThrowsDuplicateSerial_ErrorMessageContainsCorrelationId()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                var tag = new DeviceTag { Id = Guid.NewGuid(), Name = "TestTag" };
                var cut = SetupComponent(
                    new List<DeviceTag> { tag },
                    device => Task.FromException<Device>(new Exception(DuplicateSerialErrorMessage))
                );

                // Act - MacOS device
                FillAndSubmitAddForm(cut, "Apple", "MacBook", "SN12345", "2");

                // Assert: error message is shown in an error-styled alert and includes the correlation ID context
                cut.WaitForAssertion(() =>
                {
                    Assert.IsTrue(
                        cut.Markup.Contains("Error adding device:"),
                        "Error message should include the 'Error adding device:' prefix.");
                    Assert.IsTrue(
                        cut.Markup.Contains(DuplicateSerialErrorMessage),
                        "Error message should contain the specific duplicate serial error.");
                    Assert.IsTrue(
                        cut.Markup.Contains("Correlation Id"),
                        "Error message should include a Correlation Id for diagnostics.");
                });
            }
        }
    }
}
