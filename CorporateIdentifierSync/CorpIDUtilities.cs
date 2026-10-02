using DelegationStationShared.Enums;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;

namespace CorporateIdentifierSync
{
    internal static class CorpIDUtilities
    {
        public static ImportedDeviceIdentityType GetCorpIDTypeForOS(DeviceOS? os)
        {
            switch (os)
            {
                case DeviceOS.Windows:
                case DeviceOS.Unknown:
                    //treat Unknown as Windows for Corporate Identifier purposes
                    return ImportedDeviceIdentityType.ManufacturerModelSerial;
                case DeviceOS.MacOS:
                case DeviceOS.iOS:
                case DeviceOS.Android:
                    return ImportedDeviceIdentityType.SerialNumber;
                default:
                    // We are in trouble if we get here....
                    throw new ArgumentException($"Unsupported OS type: {os}");
            }
        }

        /// <summary>
        /// Builds the identifier string sent to Graph when importing a Corporate Identifier for the device.
        /// </summary>
        public static string GetCorpIdentifier(Device device)
        {
            if (device.OS == DeviceOS.Windows || device.OS == DeviceOS.Unknown)
            {
                // Putting make and model in quotes to handle commas
                string escapedMake = "\"" + device.Make + "\"";
                string escapedModel = "\"" + device.Model + "\"";
                return $"{escapedMake},{escapedModel},{device.SerialNumber}";
            }
            return device.SerialNumber;
        }

        /// <summary>
        /// Copies the fields owned by CorporateIdentifierSync from the in-memory device onto a freshly read copy.
        /// Used to retry an update after a PreconditionFailed caused by another writer (e.g. UpdateDevices
        /// patching processing fields) changing the ETag without changing Status. The fresh copy keeps its
        /// ETag and all fields written by other functions.
        /// </summary>
        public static void ApplyCorpIdFields(Device source, Device target)
        {
            target.Status = source.Status;
            target.CorporateIdentityID = source.CorporateIdentityID;
            target.CorporateIdentity = source.CorporateIdentity;
            target.LastCorpIdentitySync = source.LastCorpIdentitySync;
            target.CorpIDFailureCount = source.CorpIDFailureCount;
            target.OS = source.OS;
        }

    }
}
