using System.Net;
using DelegationStation.Services;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DelegationStation.Tests.Services
{
    [TestClass]
    public class DeviceDBServiceTests
    {
        [TestMethod]
        public async Task MarkDeviceToDeleteAsync_StaleDevice_PatchesOnlyDeletionFields()
        {
            var staleDevice = new Device { ETag = "old-etag" };
            var container = new RecordingContainer();
            var service = new DeviceDBService(container, NullLogger<DeviceDBService>.Instance);
            DateTime before = DateTime.UtcNow;

            await service.MarkDeviceToDeleteAsync(staleDevice);

            Assert.AreEqual(1, container.PatchCalls);
            Assert.AreEqual(staleDevice.Id.ToString(), container.ItemId);
            Assert.AreEqual(new PartitionKey(staleDevice.PartitionKey), container.ItemPartitionKey);
            Assert.IsNull(container.RequestOptions);
            Assert.AreEqual(2, container.Operations.Count);
            var status = container.Operations.Single(operation => operation.Path == "/Status");
            Assert.AreEqual(PatchOperationType.Set, status.OperationType);
            Assert.AreEqual(DeviceStatus.Deleting, ((PatchOperation<DeviceStatus>)status).Value);
            var timestamp = container.Operations.Single(operation => operation.Path == "/MarkedToDeleteUTC");
            Assert.AreEqual(PatchOperationType.Set, timestamp.OperationType);
            DateTime markedAt = ((PatchOperation<DateTime>)timestamp).Value;
            Assert.IsTrue(markedAt >= before && markedAt <= DateTime.UtcNow);
            Assert.AreEqual(DeviceStatus.Deleting, staleDevice.Status);
            Assert.AreEqual(markedAt, staleDevice.MarkedToDeleteUTC);
        }

        [TestMethod]
        public async Task MarkDeviceToDeleteAsync_MissingDevice_PropagatesNotFoundWithoutChangingLocalState()
        {
            var device = new Device();
            var exception = new CosmosException("Not found", HttpStatusCode.NotFound, 0, "test", 0);
            var container = new RecordingContainer { Exception = exception };
            var service = new DeviceDBService(container, NullLogger<DeviceDBService>.Instance);

            var actual = await Assert.ThrowsExactlyAsync<CosmosException>(
                () => service.MarkDeviceToDeleteAsync(device));

            Assert.AreSame(exception, actual);
            Assert.AreEqual(1, container.PatchCalls);
            Assert.AreEqual(DeviceStatus.Added, device.Status);
            Assert.IsNull(device.MarkedToDeleteUTC);
        }

        private sealed class RecordingContainer : StubContainer
        {
            public int PatchCalls { get; private set; }
            public string? ItemId { get; private set; }
            public PartitionKey ItemPartitionKey { get; private set; }
            public IReadOnlyList<PatchOperation> Operations { get; private set; } = [];
            public PatchItemRequestOptions? RequestOptions { get; private set; }
            public Exception? Exception { get; init; }

            public override Task<ItemResponse<T>> PatchItemAsync<T>(
                string id,
                PartitionKey partitionKey,
                IReadOnlyList<PatchOperation> patchOperations,
                PatchItemRequestOptions? requestOptions = null,
                CancellationToken cancellationToken = default)
            {
                PatchCalls++;
                ItemId = id;
                ItemPartitionKey = partitionKey;
                Operations = patchOperations;
                RequestOptions = requestOptions;
                return Exception is null
                    ? Task.FromResult<ItemResponse<T>>(null!)
                    : Task.FromException<ItemResponse<T>>(Exception);
            }
        }
    }
}
