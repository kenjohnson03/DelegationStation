﻿using DelegationStationShared.Models;

namespace CorporateIdentifierSync.Interfaces
{
    public interface ICosmosDbService
    {
        Task<List<Device>> GetDevicesWithoutCorpIdentity();

        Task<List<Device>> GetDevicesMarkedForDeletion();

        Task UpdateDevice(Device device);

        Task DeleteDevice(Device device);

        Task<List<Device>> GetDevicesSyncedBefore(DateTime date);


    }
}
