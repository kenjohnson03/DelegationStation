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
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(new MaintenanceBannerContent(message, null)));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.IsTrue(cut.Markup.Contains(message), $"Markup should contain the banner message. Actual: {cut.Markup}");
            Assert.AreEqual(0, cut.FindAll(".maintenance-banner-track").Count, $"Short messages should not scroll. Actual: {cut.Markup}");
            Assert.AreEqual(1, cut.FindAll(".maintenance-banner-default").Count, $"Default color class should apply when no color is specified. Actual: {cut.Markup}");
        }

        [TestMethod]
        public void LongMessageShouldScroll()
        {
            // Arrange
            string message = new string('a', 200);
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(new MaintenanceBannerContent(message, null)));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.AreEqual(1, cut.FindAll(".maintenance-banner-track").Count, $"Long messages should scroll. Actual: {cut.Markup}");
        }

        [TestMethod]
        public void CustomColorShouldBeAppliedAsInlineStyle()
        {
            // Arrange
            string message = "Scheduled maintenance tonight.";
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(new MaintenanceBannerContent(message, "#ff0000")));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            var banner = cut.Find(".maintenance-banner");
            Assert.IsTrue(banner.GetAttribute("style")!.Contains("#ff0000"), $"Banner style should include the custom color. Actual: {cut.Markup}");
            Assert.AreEqual(0, cut.FindAll(".maintenance-banner-default").Count, $"Default color class should not apply when a custom color is specified. Actual: {cut.Markup}");
        }

        [TestMethod]
        public void ParseShouldRecognizeFirstLineColorAndSeparateMessage()
        {
            // Act
            var banner = MaintenanceBannerService.Parse(new[] { "red", "Scheduled maintenance tonight." });

            // Assert
            Assert.IsNotNull(banner);
            Assert.AreEqual("red", banner!.Color);
            Assert.AreEqual("Scheduled maintenance tonight.", banner.Message);
        }

        [TestMethod]
        public void ParseShouldTreatWholeFileAsMessageWhenFirstLineIsNotAColor()
        {
            // Act
            var banner = MaintenanceBannerService.Parse(new[] { "Scheduled maintenance tonight at 9pm." });

            // Assert
            Assert.IsNotNull(banner);
            Assert.IsNull(banner!.Color);
            Assert.AreEqual("Scheduled maintenance tonight at 9pm.", banner.Message);
        }

        private sealed class TestMaintenanceBannerService : IMaintenanceBannerService
        {
            private readonly MaintenanceBannerContent? _banner;

            public TestMaintenanceBannerService(MaintenanceBannerContent? banner)
            {
                _banner = banner;
            }

            public MaintenanceBannerContent? GetBanner() => _banner;
        }
    }
}
