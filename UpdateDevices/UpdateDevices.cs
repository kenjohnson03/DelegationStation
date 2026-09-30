using Microsoft.Extensions.Logging;
using UpdateDevices.Models;
using System.Text.RegularExpressions;
using DelegationStationShared.Models;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Functions.Worker;
using UpdateDevices.Interfaces;



namespace UpdateDevices
{
    public class UpdateDevices
    {
        private int _lastRunDays = 30;

        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphService _graphService;
        private readonly IGraphBetaService _graphBetaService;



        public UpdateDevices(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphService graphService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<UpdateDevices>();
            _dbService = dbService;
            _graphService = graphService;
            _graphBetaService = graphBetaService;

            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            bool parseConfig = int.TryParse(Environment.GetEnvironmentVariable("FirstRunPastDays", EnvironmentVariableTarget.Process), out _lastRunDays);
            if (!parseConfig)
            {
                _lastRunDays = 30;
                _logger.DSLogWarning("FirstRunPastDays environment variable not set. Defaulting to 30 days", fullMethodName);
            }

        }

        [Function("UpdateDevices")]
        public async Task Run([TimerTrigger("%TriggerTime%")] TimerInfo timerInfo)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("C# Timer trigger function executed at: " + DateTime.Now, fullMethodName);
            _logger.DSLogInformation("Next timer schedule at: " + timerInfo.ScheduleStatus.Next, fullMethodName);

            FunctionSettings settings = await _dbService.GetFunctionSettings();

            // If not set, use current time - value set for last run days
            // Otherwise subtract one hour from date saved in DB
            DateTime lastRun = settings.LastRun == null ? DateTime.UtcNow.AddDays(-_lastRunDays) : ((DateTime)settings.LastRun).AddHours(-1);

            // Grabbing date before we pull devices to save off when function completes
            DateTime thisRun = DateTime.UtcNow;

            List<Microsoft.Graph.Models.ManagedDevice> devices = await _graphService.GetNewDeviceManagementObjectsAsync(lastRun);

            if (devices == null)
            {
                _logger.DSLogError("Failed to get new devices, exiting", fullMethodName);
                return;
            }
            foreach (Microsoft.Graph.Models.ManagedDevice device in devices)
            {
                try
                {
                    await RunDeviceUpdateActionsAsync(device);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Unexpected error processing enrolled device '" + device.Id + "'. Continuing with remaining devices.", ex, fullMethodName);
                }
            }

            await _dbService.UpdateFunctionSettings(thisRun);
        }

        private async Task RunDeviceUpdateActionsAsync(Microsoft.Graph.Models.ManagedDevice device)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Processing enrolled device: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'.", fullMethodName);

            // If InTune has not been updated with hardware info, the device cannot be processed.
            // We're going to add it to a separate DB entry to be checked by the StragglerHandler
            if (String.IsNullOrEmpty(device.Manufacturer) || String.IsNullOrEmpty(device.Model) || String.IsNullOrEmpty(device.SerialNumber))
            {
                _logger.DSLogWarning("Device " + device.Id + " does not have Manufacturer, Model, or Serial Number. Adding to Straggler list.", fullMethodName);
                await _dbService.AddOrUpdateStraggler(device);

                return;
            }

            var defaultActionDisable = Environment.GetEnvironmentVariable("DefaultActionDisable", EnvironmentVariableTarget.Process);

            if (String.IsNullOrEmpty(defaultActionDisable))
            {
                _logger.DSLogWarning("DefaultActionDisable environment variable not set. Defaulting to false.", fullMethodName);
            }




            DelegationStationShared.Models.Device d = await _dbService.GetDevice(device.Manufacturer, device.Model, device.SerialNumber);
            if (d == null)
            {
                _logger.DSLogWarning("Did not find any matching devices in DB for: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'.", fullMethodName);


                // TODO make personal / add to group / update attribute
                if (defaultActionDisable == "true")
                {
                    _logger.DSLogInformation("DefaultActionDisable is true. Disabling device in AAD '" + device.AzureADDeviceId + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'", fullMethodName);
                    await _graphService.UpdateAttributesOnDeviceAsync(device.Id, device.AzureADDeviceId, new List<DeviceUpdateAction> { new DeviceUpdateAction() { ActionType = DeviceUpdateActionType.Attribute, Name = "AccountEnabled", Value = "false" } });
                }
                return;
            }
            _logger.DSLogInformation("Found matching device in DB for: '" + device.Id + "' '" + device.Manufacturer + "' '" + device.Model + "' '" + device.SerialNumber + "'.", fullMethodName);

            if (device.EnrolledDateTime == null)
            {
                _logger.DSLogError("Managed device '" + device.Id + "' has no enrolledDateTime. Devices are queried by enrolledDateTime, so this indicates a Graph data error. No updates applied.", fullMethodName);
                return;
            }
            DateTime enrolledUtc = device.EnrolledDateTime.Value.UtcDateTime;

            if (d.LastSeenEnrollmentUTC != null && enrolledUtc != d.LastSeenEnrollmentUTC)
            {
                _logger.DSLogInformation("Device " + device.Id + " has re-enrolled since it was last seen. Previously seen enrollment at " +
                    d.LastSeenEnrollmentUTC + ", now enrolled at " + device.EnrolledDateTime + ". Restarting processing for the new enrollment.", fullMethodName);

                // A result recorded against the previous enrollment says nothing about this one.
                d.SuccessfullyProcessedUTC = null;
                d.ProcessingStatus = null;
            }

            if (d.Status == DeviceStatus.Deleting)
            {
                _logger.DSLogInformation("Device " + device.Id + " is marked for deletion. No updates applied.", fullMethodName);
                return;
            }
            if (d.Tags == null || d.Tags.Count == 0)
            {
                _logger.DSLogError("Device " + device.Id + " has no tag assigned. No updates applied.", fullMethodName);
                await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
                return;
            }

            bool allActionsSucceeded = true;


            string deviceObjectID = await _graphService.GetDeviceObjectID(device.AzureADDeviceId);
            if (String.IsNullOrEmpty(deviceObjectID))
            {
                _logger.DSLogError("Failed to retrieve graph device ID using .\n", fullMethodName);
                await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
                return;
            }
            _logger.DSLogInformation("Retrieved Entra Object ID '" + deviceObjectID + "' for device. DeviceID: '" + device.AzureADDeviceId + "', ManagedDeviceID: '" + device.Id + "'", fullMethodName);

            foreach (string tagId in d.Tags)
            {
                DeviceTag tag = await _dbService.GetDeviceTag(tagId);
                if (tag == null)
                {
                    _logger.DSLogError("Device " + device.Id + " is assigned to tag " + tagId + " which does not exist. No updates applied.", fullMethodName);
                    await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
                    return;
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
                            if (!Regex.IsMatch(device.UserPrincipalName, tag.AllowedUserPrincipalName))
                            {
                                _logger.DSLogWarning("Primary user " + device.UserPrincipalName + " on ManagedDevice Id " + device.Id + " does not match Tag " + tag.Name + " allowed user principal names regex '" + tag.AllowedUserPrincipalName + "'.", fullMethodName);
                                await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
                                return;
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
                    await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, false);
                    return;
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
                                renameDevice = false;
                                _logger.DSLogError("Preferred hostname '" + d.PreferredHostname + "' for device " + device.Id + " does not match device name regex " +
                                    tag.DeviceNameRegex + " for tag " + tag.Name + ". No rename applied.", fullMethodName);
                            }
                        }
                        catch (ArgumentException ex)
                        {
                            renameDevice = false;
                            _logger.DSLogException("Device name regex " + tag.DeviceNameRegex + " for tag " + tag.Name + " is invalid. No rename applied for device " +
                                device.Id + ".", ex, fullMethodName);
                        }
                        catch (RegexMatchTimeoutException ex)
                        {
                            renameDevice = false;
                            _logger.DSLogException("Regex match timed out while evaluating preferred hostname '" + d.PreferredHostname + "' against device name regex " +
                                tag.DeviceNameRegex + " for tag " + tag.Name + ". No rename applied for device " + device.Id + ".", ex, fullMethodName);
                        }
                    }

                    if (renameDevice)
                    {
                        if (!String.IsNullOrEmpty(d.PreferredHostname))
                        {
                            try
                            {
                                bool result = await _graphBetaService.SetDeviceName(device.Id, d.PreferredHostname);
                                if (!result)
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
                    _logger.DSLogInformation("No update actions configured for " + tag.Name + ".  No updates applied for device " + device.Id + ".", fullMethodName);
                    await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, allActionsSucceeded);
                    return;
                }


                //
                // Applying update actions based on tag
                //
                _logger.DSLogInformation("Apply update actions to device " + device.Id + " configured for tag " + tag.Name + "...", fullMethodName);


                foreach (DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.AdministrativeUnit))
                {
                    try
                    {
                        if (!await _graphService.AddDeviceToAzureAdministrativeUnit(device.Id, deviceObjectID, deviceUpdateAction))
                        {
                            allActionsSucceeded = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        allActionsSucceeded = false;
                        _logger.DSLogException("Unable to add Device " + device.Id + " (as " + deviceObjectID + ") to Administrative Unit: " + deviceUpdateAction.Name + " (" + deviceUpdateAction.Value + ").", ex, fullMethodName);
                    }
                }

                foreach (DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Group))
                {
                    try
                    {
                        if (!await _graphService.AddDeviceToAzureADGroup(device.Id, deviceObjectID, deviceUpdateAction))
                        {
                            allActionsSucceeded = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        allActionsSucceeded = false;
                        _logger.DSLogException("Unable to add device " + device.Id + " (as " + deviceObjectID + ") to Group: " + deviceUpdateAction.Name + " (" + deviceUpdateAction.Value + ").", ex, fullMethodName);
                    }
                }

                var attributeList = tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Attribute).ToList();
                if (attributeList.Count > 0)
                {
                    try
                    {
                        if (!await _graphService.UpdateAttributesOnDeviceAsync(device.Id, deviceObjectID, attributeList))
                        {
                            allActionsSucceeded = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        allActionsSucceeded = false;
                        _logger.DSLogException("Unable to update attributes for device " + device.Id + " (as " + deviceObjectID + ").", ex, fullMethodName);
                    }
                }
            }
            await CompleteDeviceProcessingAttemptAsync(d, enrolledUtc, allActionsSucceeded);
        }

        /// <summary>
        /// Records the outcome of this processing attempt, including the enrollment that was acted on.
        /// </summary>
        private async Task CompleteDeviceProcessingAttemptAsync(
            DelegationStationShared.Models.Device device,
            DateTime enrolledUtc,
            bool successfullyProcessed)
        {
            DateTime processedAt = DateTime.UtcNow;
            device.LastProcessingAttemptUTC = processedAt;

            // Always record the enrollment we saw, including on failure, so the next run recognises
            // this as a retry of the same enrollment rather than repeating the re-enrollment reset.
            device.LastSeenEnrollmentUTC = enrolledUtc;

            if (successfullyProcessed)
            {
                device.ProcessingStatus = ProcessingStatus.Processed;
                device.SuccessfullyProcessedUTC = processedAt;
            }

            await _dbService.UpdateDeviceProcessingState(device);
        }


    }

}
