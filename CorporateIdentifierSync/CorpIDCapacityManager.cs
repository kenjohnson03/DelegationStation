using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using Microsoft.Extensions.Logging;
using DelegationStationShared.Extensions;

namespace CorporateIdentifierSync
{
    public class CorpIdCapacityManager
    {
        private readonly ICosmosDbService _dbService;
        private readonly ILogger _logger;
        private readonly int _totalCap;

        public CorpIdCapacityManager(ICosmosDbService dbService, ILogger logger, int totalCap)
        {
            _dbService = dbService;
            _logger = logger;
            _totalCap = totalCap;
        }

        // Check the available capacity for CorpIDs by comparing the total capacity with the current count and reserved count from the database
        public async Task<int> GetAvailableCorpIDCount(CancellationToken ct)
        {
            var counter = await _dbService.GetCorpIDCounter();
            var available = _totalCap - counter.GetTotal();

            return available;
        }

        // Reserve CorpIDs by increasing the reserved count in the database.
        // This is to ensure that when multiple instances of the function are running,
        // they don't exceed the total capacity by reserving a certain number of CorpIDs before actually committing them.
        //
        // Returns the number of CorpIDs that were successfully reserved.
        // If the available capacity is less than the requested count, it will reserve as many as possible up to the total capacity and return that number.
        // If there is no available capacity, it will return 0.
        public async Task<int> ReserveCorpIDs(int count, CancellationToken ct)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            var counter = await _dbService.GetCorpIDCounter();
            var available = _totalCap - counter.GetTotal();
            int reserved = 0;

            if (available >= count)
            {
                counter.CorpIDReserve += count;
                reserved = count;
            }
            else if (available==0)
            {
                reserved = 0;
            }
            else  // available < count
            {
                counter.CorpIDReserve += available;
                reserved = available;
            }

            try
            {
                await _dbService.SetCorpIDCounter(counter);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Exception caught saving CorpID Counter: ", ex, fullMethodName);
            }
            return reserved;
        }

        // Commit the reserved CorpIDs by decreasing the reserved count and increasing the actual count in the database.
        // Optionally releases CorpIDs that are no longer active (e.g. failed re-adds) by decrementing CorpIDCount, with drift detection.
        public async Task<int> CommitCorpIDCount(int reserved, int added, CancellationToken ct)
        {
            var counter = await _dbService.GetCorpIDCounter();

            if (reserved > counter.CorpIDReserve)
            {
                _logger.DSLogWarning($"Drift detected:  Attempted to unreserved {reserved} devices but only {counter.CorpIDReserve} tracked as reserved.");
            }
            counter.CorpIDReserve = Math.Max(0, counter.CorpIDReserve - reserved);

            counter.CorpIDCount += added;
            if (counter.CorpIDCount > _totalCap)
            {
                _logger.DSLogWarning($"Drift detected:  Committing new entries to CorpID increased total above cap.");
            }
            await _dbService.SetCorpIDCounter(counter);

            var available = _totalCap - counter.GetTotal();
            return available;
        }

        // Release CorpIDs that were previously counted but are no longer active (e.g. failed re-adds or removals).
        // Decrements CorpIDCount with drift detection.
        public async Task<int> ReleaseCorpIDs(int releaseCount, CancellationToken ct)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            var counter = await _dbService.GetCorpIDCounter();
            var available = _totalCap - counter.GetTotal();

            if (releaseCount > counter.CorpIDCount)
            {
                _logger.DSLogWarning($"Drift detected: Attempting to decrement CorpIDCount by {releaseCount} but current count is {counter.CorpIDCount}.", fullMethodName);
            }

            counter.CorpIDCount = Math.Max(0, counter.CorpIDCount - releaseCount);

            try
            {
                await _dbService.SetCorpIDCounter(counter);
                available = _totalCap - counter.GetTotal();
                _logger.DSLogInformation($"Decremented CorpIDCount by {releaseCount} to {counter.CorpIDCount}.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Failed to update CorpIDCounter after releasing {releaseCount} Corp IDs.", ex, fullMethodName);
            }

            return available;
        }
    }
}
