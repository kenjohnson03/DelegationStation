using CorporateIdentifierSync;
using DelegationStationShared.Enums;
using Microsoft.Graph.Beta.Models;

namespace CorporateIdentifierSync.Tests.CorpIDUtilitiesTests;

public class CorpIDUtilitiesTests
{
    [Fact]
    public void GetCorpIDTypeForOS_Windows_ReturnsManufacturerModelSerial()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.Windows);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.ManufacturerModelSerial, result);
    }

    [Fact]
    public void GetCorpIDTypeForOS_Unknown_ReturnsManufacturerModelSerial()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.Unknown);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.ManufacturerModelSerial, result);
    }

    [Fact]
    public void GetCorpIDTypeForOS_MacOS_ReturnsSerialNumber()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.MacOS);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.SerialNumber, result);
    }

    [Fact]
    public void GetCorpIDTypeForOS_iOS_ReturnsSerialNumber()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.iOS);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.SerialNumber, result);
    }

    [Fact]
    public void GetCorpIDTypeForOS_Android_ReturnsSerialNumber()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.Android);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.SerialNumber, result);
    }

    [Fact]
    public void GetCorpIDTypeForOS_Null_ThrowsArgumentException()
    {
        // Arrange & Act
        Action act = () => CorpIDUtilities.GetCorpIDTypeForOS(null);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void GetCorpIDTypeForOS_UndefinedEnumValue_ThrowsArgumentException()
    {
        // Arrange
        DeviceOS undefinedOs = (DeviceOS)999;

        // Act
        Action act = () => CorpIDUtilities.GetCorpIDTypeForOS(undefinedOs);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
