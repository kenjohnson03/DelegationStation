using CorporateIdentifierSync;
using DelegationStationShared.Enums;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;

namespace CorporateIdentifierSync.Tests.CorpIDUtilitiesTests;

public class CorpIDUtilitiesTests
{
    /// <summary>
    /// Verifies that GetCorpIDTypeForOS returns the expected ImportedDeviceIdentityType for a given OS.
    /// </summary>
    [Theory]
    [InlineData(DeviceOS.Windows, ImportedDeviceIdentityType.ManufacturerModelSerial)]
    [InlineData(DeviceOS.Unknown, ImportedDeviceIdentityType.ManufacturerModelSerial)]
    [InlineData(DeviceOS.MacOS,   ImportedDeviceIdentityType.SerialNumber)]
    [InlineData(DeviceOS.iOS,     ImportedDeviceIdentityType.SerialNumber)]
    [InlineData(DeviceOS.Android, ImportedDeviceIdentityType.SerialNumber)]
    public void GetCorpIDTypeForOS_ReturnsExpectedIdentityType(DeviceOS os, ImportedDeviceIdentityType expected)
    {
        // Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(os);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that GetCorpIDTypeForOS throws an ArgumentException for null or undefined OS values.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData((DeviceOS)999)]
    public void GetCorpIDTypeForOS_InvalidInput_ThrowsArgumentException(DeviceOS? os)
    {
        // Act
        Action act = () => CorpIDUtilities.GetCorpIDTypeForOS(os);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    /// <summary>
    /// Verifies that ApplyCorpIdFields copies only the fields owned by CorporateIdentifierSync and
    /// leaves the target's ETag and fields owned by other writers (UI, UpdateDevices) untouched.
    /// </summary>
    [Fact]
    public void ApplyCorpIdFields_CopiesOnlyCorpIdFields()
    {
        // Arrange
        DateTime syncTime = DateTime.UtcNow;
        var source = new Device
        {
            Status = DeviceStatus.Synced,
            CorporateIdentityID = "src-corp-id",
            CorporateIdentity = "src-corp-ident",
            LastCorpIdentitySync = syncTime,
            CorpIDFailureCount = 3,
            OS = DeviceOS.Unknown,
            ETag = "src-etag",
            PreferredHostname = "SRC-HOST",
            ProcessingStatus = ProcessingStatus.Processing,
            LastProcessingAttemptUTC = null,
            SuccessfullyProcessedUTC = null,
        };
        source.Tags.Add("src-tag");

        DateTime processedAt = syncTime.AddMinutes(-5);
        DateTime markedToDelete = syncTime.AddDays(-1);
        var target = new Device
        {
            Make = "Contoso",
            Model = "Laptop",
            SerialNumber = "SN1",
            Status = DeviceStatus.NonSyncing,
            CorporateIdentityID = string.Empty,
            CorporateIdentity = string.Empty,
            LastCorpIdentitySync = DateTime.MinValue,
            CorpIDFailureCount = 0,
            OS = DeviceOS.Windows,
            ETag = "target-etag",
            PreferredHostname = "TARGET-HOST",
            ProcessingStatus = ProcessingStatus.Processed,
            LastProcessingAttemptUTC = processedAt,
            SuccessfullyProcessedUTC = processedAt,
            MarkedToDeleteUTC = markedToDelete,
        };
        target.Tags.Add("target-tag");
        Guid targetId = target.Id;
        string targetPartitionKey = target.PartitionKey;

        // Act
        CorpIDUtilities.ApplyCorpIdFields(source, target);

        // Assert – Corp ID fields copied
        Assert.Equal(DeviceStatus.Synced, target.Status);
        Assert.Equal("src-corp-id", target.CorporateIdentityID);
        Assert.Equal("src-corp-ident", target.CorporateIdentity);
        Assert.Equal(syncTime, target.LastCorpIdentitySync);
        Assert.Equal(3, target.CorpIDFailureCount);
        Assert.Equal(DeviceOS.Unknown, target.OS);

        // Assert – everything else on the target preserved
        Assert.Equal(targetId, target.Id);
        Assert.Equal(targetPartitionKey, target.PartitionKey);
        Assert.Equal("target-etag", target.ETag);
        Assert.Equal("Contoso", target.Make);
        Assert.Equal("Laptop", target.Model);
        Assert.Equal("SN1", target.SerialNumber);
        Assert.Equal("TARGET-HOST", target.PreferredHostname);
        Assert.Equal(new List<string> { "target-tag" }, target.Tags);
        Assert.Equal(ProcessingStatus.Processed, target.ProcessingStatus);
        Assert.Equal(processedAt, target.LastProcessingAttemptUTC);
        Assert.Equal(processedAt, target.SuccessfullyProcessedUTC);
        Assert.Equal(markedToDelete, target.MarkedToDeleteUTC);
    }
}
