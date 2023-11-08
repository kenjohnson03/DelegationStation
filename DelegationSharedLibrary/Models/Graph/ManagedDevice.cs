using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DelegationSharedLibrary.Models.Graph
{    
    public class ManagedDevice
    {
        // Set all properties to nullable to avoid errors when deserializing
        public string? id { get; set; }
        public string? userId { get; set; }
        public string? deviceName { get; set; }
        public string? managedDeviceOwnerType { get; set; }
        public DateTime enrolledDateTime { get; set; }
        public DateTime lastSyncDateTime { get; set; }
        public string? operatingSystem { get; set; }
        public string? complianceState { get; set; }
        public string? jailBroken { get; set; }
        public string? managementAgent { get; set; }
        public string? osVersion { get; set; }
        public bool easActivated { get; set; }
        public string? easDeviceId { get; set; }
        public DateTime easActivationDateTime { get; set; }
        public bool azureADRegistered { get; set; }
        public string? deviceEnrollmentType { get; set; }
        public string? activationLockBypassCode { get; set; }
        public string? emailAddress { get; set; }
        public string? azureADDeviceId { get; set; }
        public string? deviceRegistrationState { get; set; }
        public string? deviceCategoryDisplayName { get; set; }
        public bool isSupervised { get; set; }
        public string? exchangeLastSuccessfulSyncDateTime { get; set; }
        public string? exchangeAccessState { get; set; }
        public string? exchangeAccessStateReason { get; set; }
        public string? remoteAssistanceSessionUrl { get; set; }
        public string? remoteAssistanceSessionErrorDetails { get; set; }
        public bool isEncrypted { get; set; }
        public string? userPrincipalName { get; set; }
        public string? model { get; set; }
        public string? manufacturer { get; set; }
        public string? imei { get; set; }
        public string? complianceGracePeriodExpirationDateTime { get; set; }
        public string? serialNumber { get; set; }
        public string? phoneNumber { get; set; }
        public string? androidSecurityPatchLevel { get; set; }
        public string? userDisplayName { get; set; }
        public string? configurationManagerClientEnabledFeatures { get; set; }
        public string? wiFiMacAddress { get; set; }
        public string? deviceHealthAttestationState { get; set; }
        public string? subscriberCarrier { get; set; }
        public string? meid { get; set; }
        public long totalStorageSpaceInBytes { get; set; }
        public long freeStorageSpaceInBytes { get; set; }
        public string? managedDeviceName { get; set; }
        public string? partnerReportedThreatState { get; set; }
        public string? requireUserEnrollmentApproval { get; set; }
        public DateTime managementCertificateExpirationDate { get; set; }
        public string? iccid { get; set; }
        public string? udid { get; set; }
        public string? notes { get; set; }
        public string? ethernetMacAddress { get; set; }
        public long physicalMemoryInBytes { get; set; }
        public List<string> deviceActionResults { get; set; }

        public ManagedDevice()
        {
            deviceActionResults = new List<string>();
        }
    }

    public class ManagedDevices
    {
        [JsonProperty("@odata.context")]
        public string? odataContext { get; set; }

        [JsonProperty("@odata.count")]
        public int odataCount { get; set; }
        [JsonProperty("odata.nextLink")]
        public string? odataNextLink { get; set; }

        [JsonProperty("value")]
        public List<ManagedDevice> value { get; set; }

        public ManagedDevices()
        {
            odataContext = string.Empty;
            odataCount = 0;
            odataNextLink = string.Empty;
            value = new List<ManagedDevice>();
        }
    }
}
