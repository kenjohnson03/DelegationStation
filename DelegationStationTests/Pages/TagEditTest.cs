using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bunit;
using DelegationStation.Pages;
using DelegationStationShared;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using DelegationStation.Services;
using Microsoft.Extensions.Logging;
using DelegationStation.Fakes;
using Microsoft.Graph.Models;
using AngleSharp;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using Microsoft.Graph.Reports.GetGroupArchivedPrintJobsWithGroupIdWithStartDateTimeWithEndDateTime;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class TagEditTest : Bunit.TestContext
    {
        [TestMethod]
        public void TagShouldRender()
        {
            using (ShimsContext.Create()) 
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = @"class=""form-control"" value=""testTagName1""";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                match = @"class=""form-control"" value=""testTagDescription1""";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }
        [TestMethod]
        public void AdminShouldRenderEdit()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = @" Role:
                    <select";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AuthorizedShouldNotRenderEdit()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", Guid.NewGuid().ToString()));

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = @" Role:
                    <select";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AdminShouldShowAllUpdateActions()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = @"<option value=""Group"".*>Group</option>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                match = @"<option value=""AdministrativeUnit"".*>AdministrativeUnit</option>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                match = @"<option value=""Attribute"".*>Attribute</option>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AdminShouldShowAllAttributes()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                for(int i = 1; i < 16; i++)
                {
                    string match = $"<option value=\"ExtensionAttribute{i}\".*>ExtensionAttribute{i}</option>";
                    Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                }
            }
        }

        [TestMethod]
        public void AdminShouldShowSecurityGroups()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"Group\".*>Group</option>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AdminShouldShowAdministrativeUnits()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"AdministrativeUnit\".*>AdministrativeUnit</option>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void LimitedRoleShouldLimitExtensionAttributes()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroup = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));

                AddLimitedRoleServices(defaultId.ToString(), userGroup.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                int[] ints = new int[3];
                ints[0] = 1;
                ints[1] = 5;
                ints[2] = 12;
                for (int i = 1; i < 16; i++)
                {
                    string match = $"<option value=\"ExtensionAttribute{i}\".*>ExtensionAttribute{i}</option>";
                    if (ints.Contains(i))
                    {
                        Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                    }
                    else
                    {
                        Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected to not Match:\n{match}\nActual:\n{cut.Markup}");
                    }
                }
            }
        }

        [TestMethod]
        public void LimitedRoleShouldNotRenderSecurityGroups()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroup = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));

                AddLimitedRoleServices(defaultId.ToString(), userGroup.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"Group\".*>Group</option>";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void LimitedRoleShouldNotRenderAdministrativeUnits()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroup = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));
                authContext.SetClaims(new System.Security.Claims.Claim("roles", userGroup.ToString()));

                AddLimitedRoleServices(defaultId.ToString(), userGroup.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"AdministrativeUnit\".*>AdministrativeUnit</option>";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void UnauthorizedShouldNotRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetNotAuthorized();

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>();

                // Assert
                string match = @"<h3>Tag Edit</h3>.*";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                match = @"<p>Not Authorized</p>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                match = @"<table";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected to not Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void NoIdParameterShouldError()
        {
            // Arrange
            Guid defaultId = Guid.NewGuid();
            Guid userGroup = Guid.NewGuid();
            var authContext = this.AddTestAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
            authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));

            AddDefaultServices(defaultId.ToString());

            // Act
            var cut = RenderComponent<TagEdit>();


            // Assert
            string match = @"<h3>Tag Edit</h3>.*";
            Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            match = @"<h3>Error in navigation path</h3>";
            Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
        }

        [TestMethod]
        [ExpectedException(typeof(Bunit.ElementNotFoundException))]
        public void SaveButtonShouldNotRender()
        {
            // Arrange
            Guid defaultId = Guid.NewGuid();
            Guid userGroup = Guid.NewGuid();
            var authContext = this.AddTestAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
            authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));

            AddDefaultServices(defaultId.ToString());

            // Act
            var cut = RenderComponent<TagEdit>();
            var buttonElement = cut.Find("#SaveButton");
            
            // Assert
            Assert.IsNull(buttonElement);
        }

        [TestMethod]
        public void SaveButtonShouldRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroup = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));

                AddLimitedRoleServices(defaultId.ToString(), userGroup.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));
                var buttonElement = cut.Find("#SaveButton");
                buttonElement.Click();

                // Assert
                Assert.IsNotNull(buttonElement);
            }
        }

        private void AddDefaultServices(string defaultId = "")
        {

            //      Create fake services


            List<DeviceTag> deviceTags = new List<DeviceTag>();
            DeviceTag deviceTag1 = new DeviceTag();
            deviceTag1.Name = "testTagName1";
            deviceTag1.Description = "testTagDescription1";
            deviceTags.Add(deviceTag1);
            DeviceTag deviceTag2 = new DeviceTag();
            deviceTag2.Name = "testName2";
            deviceTag2.Description = "testDescription2";
            deviceTags.Add(deviceTag2);
            var fakeDeviceTagDBService = new DelegationStation.Services.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagsAsyncIEnumerableOfString =
                    (groupIds) => Task.FromResult<List<DeviceTag>>(deviceTags),
                GetDeviceTagAsyncString = 
                    (input) => Task.FromResult(deviceTag1)
            };

            DelegationStationShared.Models.Device device1 = new DelegationStationShared.Models.Device()
            {
                Make = "1",
                Model = "2",
                SerialNumber = "3"
            };
            List<DelegationStationShared.Models.Device> devices = new List<DelegationStationShared.Models.Device>();
            devices.Add(device1);
            var fakeDeviceDBService = new DelegationStation.Services.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringStringInt32Int32 = (a, b, c, d) =>
                    Task.FromResult(devices)
            };

            List<Role> roles = new List<Role>();
            Role role = new Role();
            role.Name = "testRole";
            role.Attributes.Add(AllowedAttributes.All);
            role.SecurityGroups = true;
            role.AdministrativeUnits = true;
            roles.Add(role);

            var fakeRoleDBService = new DelegationStation.Services.Fakes.StubIRoleDBService()
            {
                GetRolesAsync = () => Task.FromResult<List<Role>>(roles)
            };

            var fakeGraphService = new DelegationStation.Services.Fakes.StubIGraphService()
            {
                GetSecurityGroupNameString = (input) => Task.FromResult((string)input)
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
            Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            Services.AddSingleton<IRoleDBService>(fakeRoleDBService);
            Services.AddSingleton<IGraphService>(fakeGraphService);
            Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        }

        private void AddLimitedRoleServices(string defaultId = "", string userGroupId = "")
        {

            //      Create fake services
            List<Role> roles = new List<Role>();
            Role role = new Role();
            role.Name = "testRole";
            role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
            role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
            role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
            role.SecurityGroups = false;
            role.AdministrativeUnits = false;
            roles.Add(role);

            RoleDelegation roleDelegation = new RoleDelegation();
            roleDelegation.SecurityGroupId = userGroupId;
            roleDelegation.Role = role;

            List<DeviceTag> deviceTags = new List<DeviceTag>();
            DeviceTag deviceTag1 = new DeviceTag();
            deviceTag1.Name = "testTagName1";
            deviceTag1.Description = "testTagDescription1";
            deviceTag1.RoleDelegations.Add(roleDelegation);
            deviceTags.Add(deviceTag1);
            DeviceTag deviceTag2 = new DeviceTag();
            deviceTag2.Name = "testName2";
            deviceTag2.Description = "testDescription2";
            deviceTags.Add(deviceTag2);
            var fakeDeviceTagDBService = new DelegationStation.Services.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagsAsyncIEnumerableOfString =
                    (groupIds) => Task.FromResult<List<DeviceTag>>(deviceTags),
                GetDeviceTagAsyncString =
                    (input) => Task.FromResult(deviceTag1)
            };

            DelegationStationShared.Models.Device device1 = new DelegationStationShared.Models.Device()
            {
                Make = "1",
                Model = "2",
                SerialNumber = "3"
            };
            List<DelegationStationShared.Models.Device> devices = new List<DelegationStationShared.Models.Device>();
            devices.Add(device1);
            var fakeDeviceDBService = new DelegationStation.Services.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringStringInt32Int32 = (a, b, c, d) =>
                    Task.FromResult(devices)
            };

            

            var fakeRoleDBService = new DelegationStation.Services.Fakes.StubIRoleDBService()
            {
                GetRolesAsync = () => Task.FromResult<List<Role>>(roles)
            };

            var fakeGraphService = new DelegationStation.Services.Fakes.StubIGraphService()
            {
                GetSecurityGroupNameString = (input) => Task.FromResult((string)input)
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
            Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            Services.AddSingleton<IRoleDBService>(fakeRoleDBService);
            Services.AddSingleton<IGraphService>(fakeGraphService);
            Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        }
    }
}