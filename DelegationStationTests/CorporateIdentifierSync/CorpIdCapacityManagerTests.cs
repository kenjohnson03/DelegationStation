using CorporateIdentifierSync;
using CorporateIdentifierSync.Interfaces;
using CorporateIdentifierSync.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace DelegationStationTests.CorporateIdentifierSync
{
    /// <summary>
    /// Fake ICosmosDbService that stores a single CorpIDCounter in memory.
    /// All methods unused by CorpIdCapacityManager throw NotImplementedException.
    /// </summary>
    internal class FakeCosmosDbService : ICosmosDbService
    {
        public CorpIDCounter Counter { get; set; } = new CorpIDCounter();

        public Task<CorpIDCounter> GetCorpIDCounter() => Task.FromResult(Counter);

        public Task SetCorpIDCounter(CorpIDCounter counter)
        {
            Counter = counter;
            return Task.CompletedTask;
        }

        public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
        public Task UpdateDevice(Device device) => throw new NotImplementedException();
        public Task DeleteDevice(Device device) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
        public Task<List<string>> GetSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<string>> GetNonSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetNotSyncingDevices(int batchSize) => throw new NotImplementedException();
    }

    [TestClass]
    public class CorpIdCapacityManagerTests
    {
        private const int TotalCap = 1000;

        private static CorpIdCapacityManager CreateManager(FakeCosmosDbService db)
            => new CorpIdCapacityManager(db, NullLogger.Instance, TotalCap);

        // -------------------------------------------------------------------------
        // GetAvailableCorpIDCount
        // -------------------------------------------------------------------------

        [TestMethod]
        public async Task GetAvailableCorpIDCount_WhenCounterIsEmpty_ReturnsTotalCap()
        {
            var db = new FakeCosmosDbService();
            var manager = CreateManager(db);

            int available = await manager.GetAvailableCorpIDCount(CancellationToken.None);

            Assert.AreEqual(TotalCap, available);
        }

        [TestMethod]
        public async Task GetAvailableCorpIDCount_DeductsCorpIDCountAndReserve()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDCount = 300, CorpIDReserve = 100 } };
            var manager = CreateManager(db);

            int available = await manager.GetAvailableCorpIDCount(CancellationToken.None);

            Assert.AreEqual(600, available);
        }

        [TestMethod]
        public async Task GetAvailableCorpIDCount_WhenAtCap_ReturnsZero()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDCount = TotalCap } };
            var manager = CreateManager(db);

            int available = await manager.GetAvailableCorpIDCount(CancellationToken.None);

            Assert.AreEqual(0, available);
        }

        // -------------------------------------------------------------------------
        // ReserveCorpIDs
        // -------------------------------------------------------------------------

        [TestMethod]
        public async Task ReserveCorpIDs_WhenSufficientCapacity_ReservesRequestedCount()
        {
            var db = new FakeCosmosDbService();
            var manager = CreateManager(db);

            int reserved = await manager.ReserveCorpIDs(50, CancellationToken.None);

            Assert.AreEqual(50, reserved);
            Assert.AreEqual(50, db.Counter.CorpIDReserve);
        }

        [TestMethod]
        public async Task ReserveCorpIDs_WhenAtCap_ReturnsZeroAndDoesNotModifyCounter()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDCount = TotalCap } };
            var manager = CreateManager(db);

            int reserved = await manager.ReserveCorpIDs(10, CancellationToken.None);

            Assert.AreEqual(0, reserved);
            Assert.AreEqual(0, db.Counter.CorpIDReserve);
        }

        [TestMethod]
        public async Task ReserveCorpIDs_WhenPartialCapacity_ReservesOnlyAvailableSlots()
        {
            // 20 slots available
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDCount = 980 } };
            var manager = CreateManager(db);

            int reserved = await manager.ReserveCorpIDs(50, CancellationToken.None);

            Assert.AreEqual(20, reserved);
            Assert.AreEqual(20, db.Counter.CorpIDReserve);
        }

        [TestMethod]
        public async Task ReserveCorpIDs_PersistsReserveToDatabase()
        {
            var db = new FakeCosmosDbService();
            var manager = CreateManager(db);

            await manager.ReserveCorpIDs(25, CancellationToken.None);

            Assert.AreEqual(25, db.Counter.CorpIDReserve);
        }

        // -------------------------------------------------------------------------
        // CommitCorpIDCount
        // -------------------------------------------------------------------------

        [TestMethod]
        public async Task CommitCorpIDCount_DecrementsReserveAndIncrementsCount()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDReserve = 50 } };
            var manager = CreateManager(db);

            await manager.CommitCorpIDCount(50, 40, CancellationToken.None);

            Assert.AreEqual(0, db.Counter.CorpIDReserve);
            Assert.AreEqual(40, db.Counter.CorpIDCount);
        }

        [TestMethod]
        public async Task CommitCorpIDCount_ReturnsRemainingAvailable()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDReserve = 100 } };
            var manager = CreateManager(db);

            int available = await manager.CommitCorpIDCount(100, 80, CancellationToken.None);

            Assert.AreEqual(TotalCap - 80, available);
        }

        [TestMethod]
        public async Task CommitCorpIDCount_ReserveDoesNotGoBelowZero_OnDrift()
        {
            // reserved > counter.CorpIDReserve (drift scenario)
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDReserve = 10 } };
            var manager = CreateManager(db);

            await manager.CommitCorpIDCount(50, 10, CancellationToken.None);

            Assert.AreEqual(0, db.Counter.CorpIDReserve);
        }

        [TestMethod]
        public async Task CommitCorpIDCount_UnusedReservedSlotsAreReleased()
        {
            // 100 reserved, only 60 successfully added — 40 failures released
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDReserve = 100 } };
            var manager = CreateManager(db);

            await manager.CommitCorpIDCount(100, 60, CancellationToken.None);

            Assert.AreEqual(0, db.Counter.CorpIDReserve);
            Assert.AreEqual(60, db.Counter.CorpIDCount);
        }

        // -------------------------------------------------------------------------
        // ReleaseCorpIDs
        // -------------------------------------------------------------------------

        [TestMethod]
        public async Task ReleaseCorpIDs_DecrementsCorpIDCountByReleaseAmount()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDCount = 100 } };
            var manager = CreateManager(db);

            await manager.ReleaseCorpIDs(30, CancellationToken.None);

            Assert.AreEqual(70, db.Counter.CorpIDCount);
        }

        [TestMethod]
        public async Task ReleaseCorpIDs_DoesNotGoBelowZero_OnDrift()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDCount = 10 } };
            var manager = CreateManager(db);

            await manager.ReleaseCorpIDs(50, CancellationToken.None);

            Assert.AreEqual(0, db.Counter.CorpIDCount);
        }

        [TestMethod]
        public async Task ReleaseCorpIDs_ReturnsUpdatedAvailable()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDCount = 200 } };
            var manager = CreateManager(db);

            int available = await manager.ReleaseCorpIDs(50, CancellationToken.None);

            Assert.AreEqual(TotalCap - 150, available);
        }

        [TestMethod]
        public async Task ReleaseCorpIDs_DoesNotAffectCorpIDReserve()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter { CorpIDCount = 100, CorpIDReserve = 25 } };
            var manager = CreateManager(db);

            await manager.ReleaseCorpIDs(40, CancellationToken.None);

            Assert.AreEqual(25, db.Counter.CorpIDReserve);
        }
    }
}
