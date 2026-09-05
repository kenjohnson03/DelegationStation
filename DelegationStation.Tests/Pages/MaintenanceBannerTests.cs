using DelegationStation.Services;
using DelegationStation.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace DelegationStation.Tests.Pages
{
    [TestClass]
    public class MaintenanceBannerTests : BunitTestContext
    {
        [TestMethod]
        public void NoBannerFilePresentShouldNotRenderAnything()
        {
            // Arrange
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(null));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.AreEqual(string.Empty, cut.Markup.Trim(), $"No markup should render when there is no banner message. Actual: {cut.Markup}");
        }

        [TestMethod]
        public void ShortMessageShouldRenderWithoutScrolling()
        {
            // Arrange
            string message = "Scheduled maintenance tonight.";
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(message));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.IsTrue(cut.Markup.Contains(message), $"Markup should contain the banner message. Actual: {cut.Markup}");
            Assert.AreEqual(0, cut.FindAll(".maintenance-banner-track").Count, $"Short messages should not scroll. Actual: {cut.Markup}");
        }

        [TestMethod]
        public void LongMessageShouldScroll()
        {
            // Arrange
            string message = new string('a', 200);
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(message));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.AreEqual(1, cut.FindAll(".maintenance-banner-track").Count, $"Long messages should scroll. Actual: {cut.Markup}");
        }

        private sealed class TestMaintenanceBannerService : IMaintenanceBannerService
        {
            private readonly string? _message;

            public TestMaintenanceBannerService(string? message)
            {
                _message = message;
            }

            public string? GetMessage() => _message;
        }
    }
}
