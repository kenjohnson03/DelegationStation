using Bunit;
using DelegationStation.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using DelegationStation.Interfaces;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using DelegationStation.Shared;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class NavMenuTests : Bunit.TestContext
    {
        [TestMethod]
        public void DisplayRolesMenuToAdmins()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                Guid userGroupId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
                authContext.SetPolicies("DelegationStationAdmin");

                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                var httpContext = new HttpContextAccessor();
                httpContext.HttpContext = new DefaultHttpContext();

                // Add Dependent Services
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
                Services.AddSingleton<IHttpContextAccessor>(httpContext);
                Services.AddSingleton<DelegationStation.Services.RecentUpdatesNotificationService>();
                Services.AddDataProtection();
                Services.AddSingleton<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage>();
                JSInterop.Mode = JSRuntimeMode.Loose;


                // Act
                var cut = RenderComponent<NavMenu>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("Roles"), $"Role link should be rendered. Actual: {cut.Markup}");


            }
        }

        [TestMethod]
        public void DoNotDisplayRolesMenuToNonAdmins()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                Guid userGroupId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroupId.ToString()));

                
                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                var httpContext = new HttpContextAccessor();
                httpContext.HttpContext = new DefaultHttpContext();

                // Add Dependent Services
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
                Services.AddSingleton<IHttpContextAccessor>(httpContext);
                Services.AddSingleton<DelegationStation.Services.RecentUpdatesNotificationService>();
                Services.AddDataProtection();
                Services.AddSingleton<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage>();
                JSInterop.Mode = JSRuntimeMode.Loose;


                // Act
                var cut = RenderComponent<NavMenu>();

                // Assert
                Assert.IsFalse(cut.Markup.Contains("Roles"), $"Menu should not display Roles link. Actual: {cut.Markup}");
            }
        }
    }
}
