using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using DelegationStation.Interfaces;
using SettingsPage = DelegationStation.Pages.SystemSettings;
using SettingsModel = DelegationStationShared.Models.SystemSettings;

namespace DelegationStation.Tests.Pages
{
    [TestClass]
    public class SystemSettingsTests : BunitTestContext
    {
        private static IConfiguration CreateConfiguration(Guid defaultId)
        {
            var myConfiguration = new Dictionary<string, string?>
            {
                {"DefaultAdminGroupObjectId", defaultId.ToString()}
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(myConfiguration)
                .Build();
        }

        private static SettingsModel CreateSettings()
        {
            return new SettingsModel
            {
                MaxCorpIdsAllowed = 5000,
                CorpIdWarningThresholdPercent = 80,
                ExpireProcessedAfterDays = 30,
                ExpireUnprocessedAfterDays = 15
            };
        }

        private void SetupAdminAuthorization(Guid defaultId)
        {
            var authContext = this.AddAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
            authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
            authContext.SetPolicies("DelegationStationAdmin");
        }

        [TestMethod]
        public void SystemSettingsShouldRenderLoadedValues()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                SetupAdminAuthorization(defaultId);

                SettingsModel settings = CreateSettings();
                var fakeSystemSettingsDBService = new DelegationStation.Interfaces.Fakes.StubISystemSettingsDBService()
                {
                    GetSystemSettingsAsync = () => Task.FromResult<SettingsModel?>(settings)
                };

                Services.AddSingleton<ISystemSettingsDBService>(fakeSystemSettingsDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                // Act
                var cut = Render<SettingsPage>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("5000"), $"MaxCorpIdsAllowed should be rendered.\nActual:\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("80"), "CorpIdWarningThresholdPercent should be rendered.");
                Assert.IsTrue(cut.Markup.Contains("30"), "ExpireProcessedAfterDays should be rendered.");
                Assert.IsTrue(cut.Markup.Contains("15"), "ExpireUnprocessedAfterDays should be rendered.");
            }
        }

        [TestMethod]
        public void UnauthorizedShouldNotRenderSettings()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));

                var fakeSystemSettingsDBService = new DelegationStation.Interfaces.Fakes.StubISystemSettingsDBService()
                {
                    GetSystemSettingsAsync = () => Task.FromResult<SettingsModel?>(CreateSettings())
                };

                Services.AddSingleton<ISystemSettingsDBService>(fakeSystemSettingsDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                // Act
                var cut = Render<SettingsPage>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("You are not authorized to view this page."),
                    $"Non-admin users should see the not authorized message.\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void SaveShouldPersistSettings()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                SetupAdminAuthorization(defaultId);

                SettingsModel? savedSettings = null;
                var fakeSystemSettingsDBService = new DelegationStation.Interfaces.Fakes.StubISystemSettingsDBService()
                {
                    GetSystemSettingsAsync = () => Task.FromResult<SettingsModel?>(CreateSettings()),
                    AddOrUpdateSystemSettingsAsyncSystemSettings = (s) =>
                    {
                        savedSettings = s;
                        return Task.FromResult(s);
                    }
                };

                Services.AddSingleton<ISystemSettingsDBService>(fakeSystemSettingsDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                var cut = Render<SettingsPage>();

                // Act
                cut.Find("button.btn-success").Click();

                // Assert
                Assert.IsNotNull(savedSettings, "Save should call the DB service to persist settings.");
                Assert.AreEqual(5000, savedSettings!.MaxCorpIdsAllowed, "Persisted MaxCorpIdsAllowed should match the form value.");
                Assert.IsTrue(cut.Markup.Contains("System settings saved."),
                    $"A success message should be shown after saving.\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void NonIntegerInputShouldShowValidationErrorAndNotSave()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                SetupAdminAuthorization(defaultId);

                bool saveCalled = false;
                var fakeSystemSettingsDBService = new DelegationStation.Interfaces.Fakes.StubISystemSettingsDBService()
                {
                    GetSystemSettingsAsync = () => Task.FromResult<SettingsModel?>(CreateSettings()),
                    AddOrUpdateSystemSettingsAsyncSystemSettings = (s) =>
                    {
                        saveCalled = true;
                        return Task.FromResult(s);
                    }
                };

                Services.AddSingleton<ISystemSettingsDBService>(fakeSystemSettingsDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                var cut = Render<SettingsPage>();

                // Act
                cut.Find("#MaxCorpIdsAllowed").Change("abc");
                cut.Find("button.btn-success").Click();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("Enter a positive whole number."),
                    $"A validation error should be shown for non-integer input.\nActual:\n{cut.Markup}");
                Assert.IsFalse(saveCalled, "Save should not be called when a field is invalid.");
            }
        }
    }
}
