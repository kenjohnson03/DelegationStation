using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UpdateDevices.Interfaces;

namespace UpdateDevices
{
    public class StragglerHandler
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphService _graphService;
        private readonly IGraphBetaService _graphBetaService;
        private readonly int _maxUDAttempts;
        private readonly int _maxSHAttempts;

        public StragglerHandler(
            ILoggerFactory loggerFactory,
            ICosmosDbService dbService,
            IGraphService graphService,
            IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<StragglerHandler>();
            _dbService = dbService;
            _graphService = graphService;
            _graphBetaService = graphBetaService;

            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            bool parseConfig = int.TryParse(Environment.GetEnvironmentVariable("MaxUpdateDeviceAttempts", EnvironmentVariableTarget.Process), out _maxUDAttempts);
            if (!parseConfig)
            {
                _maxUDAttempts = 5;
                _logger.DSLogWarning("MaxUpdateDeviceAttempts environment variable not set. Defaulting to 5 attempts.", fullMethodName);
            }

            bool parseConfig2 = int.TryParse(Environment.GetEnvironmentVariable("MaxStragglerHandlerAttempts", EnvironmentVariableTarget.Process), out _maxSHAttempts);
            if (!parseConfig2)
            {
                _maxSHAttempts = 5;
                _logger.DSLogWarning("MaxStragglerHandlerAttempts environment variable not set. Defaulting to 5 attempts.", fullMethodName);
            }
        }

        [Function("StragglerHandler")]
        public async Task RunAsync([TimerTrigger("%SHTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("C# Timer trigger function executed at: " + DateTime.Now, fullMethodName);

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}", fullMethodName);
            }

            await ProcessStragglers();

        }
        private async Task ProcessStragglers()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Processing stragglers...", fullMethodName);

            // Get Stragglers from DB with count > # retries attempted by UpdateDevices
            List<Straggler> stragglers = await _dbService.GetStragglerList(_maxUDAttempts);

            // For each device
            foreach (Straggler straggler in stragglers)
            {

                // Use Managed ID to get the device object from Graph
                ManagedDevice? device = null;
                try
                {
                    device = await _graphService.GetManagedDevice(straggler.ManagedDeviceID);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Error getting ManagedDevice for Straggler.  Will retry. " + straggler.ManagedDeviceID, ex, fullMethodName);
                    continue;
                }

                if (device == null)
                {
                    _logger.DSLogWarning("Device is no longer in the system. Removing from straggler list: " + straggler.ManagedDeviceID, fullMethodName);
                    try
                    {
                        await _dbService.DeleteStraggler(straggler);
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException("Error deleting straggler: " + straggler.ManagedDeviceID, ex, fullMethodName);
                    }
                    continue;
                }

                // If Hardware info is still missing
                if (String.IsNullOrEmpty(device.Manufacturer) || String.IsNullOrEmpty(device.Model) || String.IsNullOrEmpty(device.SerialNumber))
                {
                    //Update Straggler count +1 and set LastCheckDateTime
                    await _dbService.UpdateStraggler(straggler);

                    // Elevate logs if hardware in is still missing 24 hours after enrollment
                    TimeSpan timespan = straggler.LastSeenDateTime - straggler.EnrollmentDateTime;
                    if (timespan.TotalHours < 24)
                    {
                        _logger.DSLogWarning("Straggler " + straggler.ManagedDeviceID + " has been missing M/M/SN for " + timespan.TotalHours + " hours", fullMethodName);
                    }
                    else
                    {
                        _logger.DSLogError("Straggler " + straggler.ManagedDeviceID + " has been missing M/M/SN for " + timespan.TotalHours + " hours", fullMethodName);
                    }

                }
                else
                {
                    // Try applying changes
                    bool result = await RunDeviceUpdateActionsAsync(device);

                    // If successful -> delete straggler
                    if(result)
                    {
                        await _dbService.DeleteStraggler(straggler);
                    }
                    else
                    {
                        if (straggler.SHErrorCount < (_maxSHAttempts - 1))
                        {
                            _logger.DSLogError("Error was identified during application of updates to device: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'.  Leaving in DB in order to retry.", fullMethodName);
                            await _dbService.UpdateStragglerAsErrored(straggler);
                        }
                        else
                        {
                            _logger.DSLogError("Error was identified during application of updates to device: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'.  Retries have run out.", fullMethodName);
                            await _dbService.DeleteStraggler(straggler);
                        }
                    }

                }
            }
        }


        private async Task<bool> RunDeviceUpdateActionsAsync(ManagedDevice device)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Processing enrolled device: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'.", fullMethodName);

            if (device.EnrolledDateTime == null)
            {
                _logger.DSLogError("Managed device '" + device.Id + "' has no enrolledDateTime. This indicates a Graph data error. No updates applied.", fullMethodName);
                return false;
            }
            DateTime enrolledUtc = device.EnrolledDateTime.Value.UtcDateTime;

            bool result = true;

            var defaultActionDisable = Environment.GetEnvironmentVariable("DefaultActionDisable", EnvironmentVariableTarget.Process);
            if (String.IsNullOrEmpty(defaultActionDisable))
            {
                _logger.DSLogWarning("DefaultActionDisable environment variable not set. Defaulting to false.", fullMethodName);
            }


            DelegationStationShared.Models.Device d = await _dbService.GetDevice(device.Manufacturer, device.Model, device.SerialNumber);
            if (d == null)
            {
                _logger.DSLogWarning("Did not find any matching devices in DB for: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'.", fullMethodName);

                if (defaultActionDisable == "true")
                {
                    _logger.DSLogInformation("DefaultActionDisable is true. Disabling device in AAD '" + device.AzureADDeviceId + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'", fullMethodName);
                    await _graphService.UpdateAttributesOnDeviceAsync(device.Id, device.AzureADDeviceId, new List<DeviceUpdateAction> { new DeviceUpdateAction() { ActionType = DeviceUpdateActionType.Attribute, Name = "AccountEnabled", Value = "false" } });
                }

                // Return failure in case error is retryable
                return false;
            }

            _logger.DSLogInformation("Found matching device in DB for: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'.", fullMethodName);

            if (d.LastSeenEnrollmentUTC > enrolledUtc)
            {
                _logger.DSLogInformation("Managed device " + device.Id + " belongs to an older enrollment (" + enrolledUtc +
                    "); the current enrollment is " + d.LastSeenEnrollmentUTC + ". Removing obsolete straggler without applying updates.", fullMethodName);
                // Returning true signals calling code to delete entry
                return true;
            }

            if (d.LastSeenEnrollmentUTC != null && enrolledUtc != d.LastSeenEnrollmentUTC)
            {
                _logger.DSLogInformation("Device " + device.Id + " has re-enrolled since it was last seen. Previously seen enrollment at " +
                    d.LastSeenEnrollmentUTC + ", now enrolled at " + enrolledUtc + ". Restarting processing for the new enrollment.", fullMethodName);
                d.SuccessfullyProcessedUTC = null;
                d.ProcessingStatus = null;
            }

            if (d.Status == DeviceStatus.Deleting)
            {
                _logger.DSLogInformation("Device " + device.Id + " is marked for deletion. No updates applied.", fullMethodName);
                return true;
            }

            if (d.Tags == null || d.Tags.Count == 0)
            {
                _logger.DSLogError("Device " + device.Id + " has no tag assigned. No updates applied.", fullMethodName);
                return await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
            }

            string deviceObjectID = await _graphService.GetDeviceObjectID(device.AzureADDeviceId);
            if (String.IsNullOrEmpty(deviceObjectID))
            {
                _logger.DSLogError("Failed to retrieve graph device ID using .\n", fullMethodName);

                // return as failure in case it's a retryable error
                return await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
            }
            _logger.DSLogInformation("Retrieved Entra Object ID '" + deviceObjectID + "' for device. DeviceID: '" + device.AzureADDeviceId + "', ManagedDeviceID: '" + device.Id + "'", fullMethodName);

            foreach (string tagId in d.Tags)
            {
                DeviceTag tag = await _dbService.GetDeviceTag(tagId);
                if (tag == null)
                {
                    _logger.DSLogError("Device " + device.Id + " is assigned to tag " + tagId + " which could not be retrieved from DB. No updates applied.", fullMethodName);

                    //return as failure in case it's a retryable error
                    return await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
                }

                //
                // To prevent PAWs from being updated, check the enrollment user and ensure there is a match to permitted regex
                // Intended to protect against PAW users using this to apply changes to their PAW
                // Allow any where the user is not set
                //
                try
                {
                    if (!string.IsNullOrEmpty(tag.AllowedUserPrincipalName))
                    {
                        // If the user principal name is not in the allowed list, skip the tag
                        if (!string.IsNullOrEmpty(device.UserPrincipalName))
                        {
                            if(!Regex.IsMatch(device.UserPrincipalName, tag.AllowedUserPrincipalName))
                            {
                                _logger.DSLogWarning("Primary user " + device.UserPrincipalName + " on ManagedDevice Id " + device.Id + " does not match Tag " + tag.Name + " allowed user principal names regex '" + tag.AllowedUserPrincipalName + "'.", fullMethodName);
                                return await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
                            }
                            _logger.DSLogInformation("Primary user " + device.UserPrincipalName + " on ManagedDevice Id " + device.Id + " matches Tag " + tag.Name + " allowed user principal names regex '" + tag.AllowedUserPrincipalName + "'.", fullMethodName);
                        }
                        else
                        {
                            _logger.DSLogInformation("Primary user was null or empty indicating bulk enrollment of Managed Device Id: " + device.Id, fullMethodName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("UserPrincipalName " + device.UserPrincipalName + " on ManagedDevice Id " + device.Id + " on " + tag.Id + " allowed user principal names " + tag.AllowedUserPrincipalName + ".", ex, fullMethodName);

                    // returning failure to retry - unsure what would cause this
                    return await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
                }

                //
                // Rename device based on tag settings
                //

                if (tag.DeviceRenameEnabled)
                {
                    bool renameDevice = false;
                    if (string.IsNullOrEmpty(tag.DeviceNameRegex))
                    {
                        renameDevice = true;
                        _logger.DSLogInformation("No device name regex set for tag " + tag.Name + ". Proceeding with rename for device " + device.Id + ".", fullMethodName);
                    }
                    else
                    {
                        try
                        {
                            if (Regex.IsMatch(d.PreferredHostname, tag.DeviceNameRegex))
                            {
                                renameDevice = true;
                                _logger.DSLogInformation("Preferred hostname '" + d.PreferredHostname + "' for device " + device.Id + " matches device name regex " +
                                    tag.DeviceNameRegex + " for tag " + tag.Name + ". Proceeding with rename.", fullMethodName);
                            }
                            else
                            {
                                _logger.DSLogError("Preferred hostname '" + d.PreferredHostname + "' for device " + device.Id + " does not match device name regex " +
                                    tag.DeviceNameRegex + " for tag " + tag.Name + ". No rename applied.", fullMethodName);
                            }
                        }
                        catch (ArgumentException ex)
                        {
                            _logger.DSLogException("Device name regex " + tag.DeviceNameRegex + " for tag " + tag.Name + " is invalid. No rename applied for device " +
                                device.Id + ".", ex, fullMethodName);
                        }
                        catch (RegexMatchTimeoutException ex)
                        {
                            _logger.DSLogException("Regex match timed out while evaluating preferred hostname '" + d.PreferredHostname + "' against device name regex " +
                                tag.DeviceNameRegex + ". No rename applied for device " + device.Id + ".", ex, fullMethodName);
                        }
                    }

                    if (renameDevice)
                    {
                        if (!String.IsNullOrEmpty(d.PreferredHostname))
                        {
                            try
                            {
                                bool renameResult = await _graphBetaService.SetDeviceName(device.Id, d.PreferredHostname);
                                if (!renameResult)
                                {
                                    _logger.DSLogError("Failed to rename device: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber +
                                        " from '" + device.DeviceName + "' to '" + d.PreferredHostname + "'.", fullMethodName);
                                }
                                else
                                {
                                    _logger.DSLogInformation("Updated device name for: '" + device.Id + " from '" + device.DeviceName + "' to '" + d.PreferredHostname + "'.", fullMethodName);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.DSLogException("Unable to rename device: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber +
                                    " from '" + device.DeviceName + "' to '" + d.PreferredHostname + "'.", ex, fullMethodName);
                            }
                        }
                        else
                        {
                            _logger.DSLogInformation("Skipping rename since Preferred Hostname is null/empty: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber, fullMethodName);
                        }
                    }
                }
                else
                {
                    _logger.DSLogInformation("Device renaming is disabled for tag " + tag.Name + ". No rename applied for device " + device.Id + ".", fullMethodName);
                }

                if (tag.UpdateActions == null || tag.UpdateActions.Count < 1)
                {
                    _logger.DSLogWarning("No update actions configured for " + tag.Name + ".  No updates applied for device " + device.Id + ".", fullMethodName);

                    // return as successful since no changes need to be applied
                    return await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, true);
                }


                //
                // Applying update actions based on tag
                //
                _logger.DSLogInformation("Apply update actions to device " + device.Id + " configured for tag " + tag.Name + "...", fullMethodName);

                // for now treating these as successful if they fail, since typically it's a permissions/configuration issue
                // may need to revisit
                foreach (DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.AdministrativeUnit))
                {
                    try
                    {
                        if (!await _graphService.AddDeviceToAzureAdministrativeUnit(device.Id, deviceObjectID, deviceUpdateAction))
                        {
                            result = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException("Unable to add Device " + device.Id + " (as " + deviceObjectID + ") to Administrative Unit: " + deviceUpdateAction.Name + " (" + deviceUpdateAction.Value + ").", ex, fullMethodName);

                        // mark failure to trigger retry
                        result = false;
                    }
                }

                foreach (DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Group))
                {
                    try
                    {
                        if (!await _graphService.AddDeviceToAzureADGroup(device.Id, deviceObjectID, deviceUpdateAction))
                        {
                            result = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException("Unable to add device " + device.Id + " (as " + deviceObjectID + ") to Group: " + deviceUpdateAction.Name + " (" + deviceUpdateAction.Value + ").", ex, fullMethodName);

                        // mark failure to trigger retry
                        result = false;
                    }
                }

                var attributeList = tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Attribute).ToList();
                if (attributeList.Count > 0)
                {
                    try
                    {
                        if (!await _graphService.UpdateAttributesOnDeviceAsync(device.Id, deviceObjectID, attributeList))
                        {
                            result = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException("Unable to update attributes for device " + device.Id + " (as " + deviceObjectID + ").", ex, fullMethodName);
                        result = false;
                    }
                }
            }

            return await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, result);
        }

        private async Task<bool> CompleteDeviceProcessingAttemptAsync(
            DelegationStationShared.Models.Device device,
            DateTime enrolledUtc,
            bool successfullyProcessed)
        {
            DateTime processedAt = DateTime.UtcNow;
            device.LastProcessingAttemptUTC = processedAt;
            device.LastSeenEnrollmentUTC = enrolledUtc;

            if (successfullyProcessed)
            {
                device.ProcessingStatus = ProcessingStatus.Processed;
                device.SuccessfullyProcessedUTC = processedAt;
            }

            bool stateUpdated = await _dbService.UpdateDeviceProcessingState(device);
            return successfullyProcessed && stateUpdated;
        }
    }
}
