using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using DelegationStationShared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using System.Reflection;
using Xunit;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.ReconcileSyncStateTests
{
    [Collection("EnvVarTests")]
    public class ReconcileSyncStateTests
    {
        // -----------------------------------------------------------------------
        // Inner helpers
        // -----------------------------------------------------------------------

        /// <summary>Captures every log call so tests can assert on log output.</summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Logs { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => Logs.Add((logLevel, formatter(state, exception)));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }

        /// <summary>Returns a <see cref="CapturingLogger"/> for every category.</summary>
        private sealed class CapturingLoggerFactory : ILoggerFactory
        {
            public CapturingLogger Logger { get; } = new();
            public bool CreateLoggerCalled { get; private set; }

            public void AddProvider(ILoggerProvider provider) { }

            public ILogger CreateLogger(string categoryName)
            {
                CreateLoggerCalled = true;
                return Logger;
            }

            public void Dispose() { }
        }

        /// <summary>Minimal <see cref="IAsyncDisposable"/> used as a lock handle.</summary>
        private sealed class AsyncDisposableHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>Stub for <see cref="IFunctionSingletonLock"/> that either acquires or rejects the lock.</summary>
        private sealed class StubSingletonLock : IFunctionSingletonLock
        {
            private readonly bool _acquireLock;

            public StubSingletonLock(bool acquireLock) => _acquireLock = acquireLock;

            public Task<IAsyncDisposable?> TryAcquireAsync(
                string lockName,
                CancellationToken cancellationToken = default)
                => Task.FromResult<IAsyncDisposable?>(_acquireLock ? new AsyncDisposableHandle() : null);
        }

        /// <summary>
        /// Configurable stub for <see cref="ICosmosDbService"/>.
        /// Methods needed by the paths under test return configurable values;
        /// all unused methods throw <see cref="NotImplementedException"/>.
        /// </summary>
        private sealed class StubCosmosDbService : ICosmosDbService
        {
            public List<string> NonSyncingTagsToReturn { get; set; } = [];
            public List<Device> SyncedDevicesInTagsToReturn { get; set; } = [];
            public List<string> SyncingTagsToReturn { get; set; } = [];
            public List<Device> NotSyncingDevicesInTagsToReturn { get; set; } = [];
            public CorpIDCounter CounterToReturn { get; set; } = new CorpIDCounter(0);
            public Action<Device>? OnUpdateDevice { get; set; }
            public Action? OnCounterWrite { get; set; }
            public Action? OnGetNotSyncingDevices { get; set; }
            public List<Device> UpdatedDevices { get; } = [];
            public int SyncingTagQueries { get; private set; }
            public int CounterWrites { get; private set; }

            public Task<List<string>> GetNonSyncingDeviceTags()
                => Task.FromResult(NonSyncingTagsToReturn);

            public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize)
                => Task.FromResult(SyncedDevicesInTagsToReturn);

            public Task<List<string>> GetSyncingDeviceTags()
            {
                SyncingTagQueries++;
                return Task.FromResult(SyncingTagsToReturn);
            }

            public Task<CorpIDCounter> GetCorpIDCounter()
                => Task.FromResult(CounterToReturn);

            public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
            {
                CounterWrites++;
                CounterToReturn = counter;
                OnCounterWrite?.Invoke();
                return Task.FromResult(true);
            }

            public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
            public Task UpdateDevice(Device device)
            {
                UpdatedDevices.Add(device);
                OnUpdateDevice?.Invoke(device);
                return Task.CompletedTask;
            }
            public Task DeleteDevice(Device device) => throw new NotImplementedException();
            public Task<Device?> GetDevice(Guid id, string partitionKey) => throw new NotImplementedException();
            public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
            public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
            public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
            public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagsWithSyncEnabled, int batchSize)
            {
                OnGetNotSyncingDevices?.Invoke();
                return Task.FromResult(NotSyncingDevicesInTagsToReturn);
            }
            public Task<int> GetSyncedDeviceCountAsync() => throw new NotImplementedException();
        }

        /// <summary>Configurable Graph stub for reconciliation scenarios.</summary>
        private sealed class StubGraphBetaService : IGraphBetaService
        {
            public Func<string, Task<DeleteCorpIdResult>>? OnDelete { get; set; }
            public Action? OnAdd { get; set; }
            public int DeleteCalls { get; private set; }
            public int AddCalls { get; private set; }

            public Task<ImportedDeviceIdentity> AddCorporateIdentifier(
                ImportedDeviceIdentityType type, string identifier)
            {
                AddCalls++;
                OnAdd?.Invoke();
                return Task.FromResult(new ImportedDeviceIdentity
                {
                    Id = "added-id",
                    ImportedDeviceIdentifier = identifier
                });
            }

            public Task<DeleteCorpIdResult> DeleteCorporateIdentifier(string identifierID)
            {
                DeleteCalls++;
                return OnDelete is null
                    ? Task.FromResult(DeleteCorpIdResult.Success)
                    : OnDelete(identifierID);
            }

            public Task<bool> CorporateIdentifierExists(string identiferID)
                => throw new NotImplementedException();

            public Task<int> GetCorporateDeviceIdentifierCountAsync()
                => throw new NotImplementedException();
        }

        /// <summary>
        /// Saves and restores a set of environment variables for test isolation.
        /// </summary>
        private sealed class EnvVarScope : IDisposable
        {
            private readonly Dictionary<string, string?> _saved = [];

            public void Set(string name, string? value)
            {
                if (!_saved.ContainsKey(name))
                {
                    _saved[name] = Environment.GetEnvironmentVariable(name);
                }

                Environment.SetEnvironmentVariable(name, value);
            }

            public void Dispose()
            {
                foreach (KeyValuePair<string, string?> entry in _saved)
                {
                    Environment.SetEnvironmentVariable(entry.Key, entry.Value);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Factory helpers
        // -----------------------------------------------------------------------

        private static ReconcileSyncState CreateSut(
            CapturingLoggerFactory loggerFactory,
            StubCosmosDbService? db = null,
            bool acquireLock = true,
            StubGraphBetaService? graph = null)
        {
            db ??= new StubCosmosDbService();
            return new ReconcileSyncState(
                loggerFactory,
                db,
                graph ?? new StubGraphBetaService(),
                new StubSingletonLock(acquireLock),
                new TelemetryClient(new TelemetryConfiguration { DisableTelemetry = true }));
        }

        private static TimerInfo MakeTimerInfo(bool withScheduleStatus)
        {
            var timer = new TimerInfo();
            if (withScheduleStatus)
            {
                timer.ScheduleStatus = new ScheduleStatus
                {
                    Next = DateTime.UtcNow.AddHours(1),
                };
            }

            return timer;
        }

        // -----------------------------------------------------------------------
        // Reflection helpers
        // -----------------------------------------------------------------------

        private static bool GetIsCorpIDSyncEnabled(ReconcileSyncState sut)
        {
            var field = typeof(ReconcileSyncState)
                .GetField("_IsCorpIDSyncEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            return (bool)field!.GetValue(sut)!;
        }

        private static int GetBatchSize(ReconcileSyncState sut)
        {
            var field = typeof(ReconcileSyncState)
                .GetField("_BatchSize", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)field!.GetValue(sut)!;
        }

        private static int GetMaxCorpIDsAllowed(ReconcileSyncState sut)
        {
            var field = typeof(ReconcileSyncState)
                .GetField("_MaxCorpIDsAllowed", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)field!.GetValue(sut)!;
        }


        #region GetEnvironmentVariableTests
        // -----------------------------------------------------------------------
        // GetEnvironmentVariables – EnableCorpIDSync
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that GetEnvironmentVariables sets _IsCorpIDSyncEnabled to the expected value.
        /// Invalid or missing inputs default to false; valid boolean strings are parsed correctly.
        /// </summary>
        [Theory]
        [InlineData(null, false)]           // not set → default false
        [InlineData("not-a-bool", false)]   // unparseable → default false
        [InlineData("true", true)]          // valid → true
        [InlineData("false", false)]        // valid → false
        public void GetEnvironmentVariables_SetsIsCorpIDSyncEnabledFromEnvVar(string? envVarValue, bool expected)
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", envVarValue);
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Equal(expected, GetIsCorpIDSyncEnabled(sut));
        }

        // -----------------------------------------------------------------------
        // GetEnvironmentVariables – ReconcileSyncBatchSize
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that GetEnvironmentVariables sets _BatchSize to the expected value.
        /// Invalid, missing, zero, and negative inputs fall back to the default of 1000;
        /// a valid positive value is used directly.
        /// </summary>
        [Theory]
        [InlineData(null, 1000)]            // not set → default
        [InlineData("not-a-number", 1000)]  // unparseable → default
        [InlineData("0", 1000)]             // zero (must be > 0) → default
        [InlineData("-5", 1000)]            // negative → default
        [InlineData("500", 500)]            // valid positive → used as-is
        [InlineData("100", 100)]            // valid positive → used as-is
        public void GetEnvironmentVariables_SetsBatchSizeFromEnvVar(string? envVarValue, int expectedBatchSize)
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", envVarValue);
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Equal(expectedBatchSize, GetBatchSize(sut));
        }

        // -----------------------------------------------------------------------
        // GetEnvironmentVariables – MAX_CORPIDS_ALLOWED
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that GetEnvironmentVariables sets _MaxCorpIDsAllowed to the expected value.
        /// Invalid, missing, zero, and negative inputs fall back to the default of 10000;
        /// a valid positive value is used directly.
        /// </summary>
        [Theory]
        [InlineData(null, 10000)]           // not set → default
        [InlineData("bad-value", 10000)]    // unparseable → default
        [InlineData("0", 10000)]            // zero (must be > 0) → default
        [InlineData("-10", 10000)]          // negative → default
        [InlineData("5000", 5000)]          // valid positive → used as-is
        public void GetEnvironmentVariables_SetsMaxCorpIDsAllowedFromEnvVar(string? envVarValue, int expectedMax)
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", envVarValue);

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Equal(expectedMax, GetMaxCorpIDsAllowed(sut));
        }
        #endregion GetEnvironmentVariableTests

        #region SingletonLockTests
        // -----------------------------------------------------------------------
        // Run tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that Run logs a warning and does not proceed when the singleton lock cannot be acquired.
        /// </summary>
        [Fact]
        public async Task Run_WhenLockNotAcquired_LogsWarningAndDoesNotProceed()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory, acquireLock: false);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: false);

            await sut.Run(timer);

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Warning && l.Message.Contains("Another instance of ReconcileSyncState is already running"));

            // GetEnvironmentVariables should NOT have been called (no execution-start message)
            Assert.DoesNotContain(
                loggerFactory.Logger.Logs,
                l => l.Message.Contains("C# Timer trigger function executed at"));
        }
        #endregion SingletonLockTests

        #region EarlyExitTests
        /// <summary>
        /// Verifies that Run logs an informational message and does not call the database when sync is not enabled.
        /// </summary>
        [Fact]
        public async Task Run_WhenSyncNotEnabled_LogsInfoAndDoesNotCallDb()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "false");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var db = new StubCosmosDbService();
            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory, db);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: false);

            await sut.Run(timer);

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Syncing not enabled. No work to do."));

            // The completion message should NOT appear since we returned early
            Assert.DoesNotContain(
                loggerFactory.Logger.Logs,
                l => l.Message.Contains("ReconcileSyncState completed"));
        }
        #endregion EarlyExitTests

        [Fact]
        public async Task Run_WhenCanceledBeforeRun_DoesNotStartReconciliation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var loggerFactory = new CapturingLoggerFactory();

            await CreateSut(loggerFactory).Run(new TimerInfo(), cts.Token);

            Assert.Contains(loggerFactory.Logger.Logs, l => l.Message.Contains("Cancellation requested"));
            Assert.DoesNotContain(loggerFactory.Logger.Logs, l => l.Message.Contains("Section 1"));
        }

        [Theory]
        [InlineData(DeleteCorpIdResult.Success, 4)]
        [InlineData(DeleteCorpIdResult.NotFound, 5)]
        public async Task Run_WhenCanceledDuringRemoval_FinishesDeviceReleasesCapacityAndSkipsAdditions(
            DeleteCorpIdResult deleteResult, int expectedCount)
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            using var cts = new CancellationTokenSource();
            var firstDevice = new Device { CorporateIdentityID = "first-id" };
            var db = new StubCosmosDbService
            {
                SyncedDevicesInTagsToReturn = [firstDevice, new Device { CorporateIdentityID = "second-id" }],
                CounterToReturn = new CorpIDCounter(0) { CorpIDCount = 5, CorpIDReserve = 3 }
            };
            var graph = new StubGraphBetaService
            {
                OnDelete = _ =>
                {
                    cts.Cancel();
                    return Task.FromResult(deleteResult);
                }
            };

            await CreateSut(new CapturingLoggerFactory(), db, graph: graph)
                .Run(new TimerInfo(), cts.Token);

            Assert.Equal(1, graph.DeleteCalls);
            Assert.Same(firstDevice, Assert.Single(db.UpdatedDevices));
            Assert.Equal(DelegationStationShared.Enums.DeviceStatus.NonSyncing, firstDevice.Status);
            Assert.Equal(expectedCount, db.CounterToReturn.CorpIDCount);
            Assert.Equal(3, db.CounterToReturn.CorpIDReserve);
            Assert.Equal(deleteResult == DeleteCorpIdResult.Success ? 1 : 0, db.CounterWrites);
            Assert.Equal(0, db.SyncingTagQueries);
        }

        [Fact]
        public async Task Run_WhenLaterRemovalThrows_ReleasesEarlierRemovalAndPropagatesException()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            var db = new StubCosmosDbService
            {
                SyncedDevicesInTagsToReturn =
                [
                    new Device { CorporateIdentityID = "first-id" },
                    new Device { CorporateIdentityID = "second-id" }
                ],
                CounterToReturn = new CorpIDCounter(0) { CorpIDCount = 5, CorpIDReserve = 3 }
            };
            var graph = new StubGraphBetaService
            {
                OnDelete = id => id == "first-id"
                    ? Task.FromResult(DeleteCorpIdResult.Success)
                    : throw new InvalidOperationException("Graph delete failed")
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateSut(new CapturingLoggerFactory(), db, graph: graph).Run(new TimerInfo()));

            Assert.Equal("Graph delete failed", ex.Message);
            Assert.Equal(4, db.CounterToReturn.CorpIDCount);
            Assert.Equal(3, db.CounterToReturn.CorpIDReserve);
            Assert.Equal(1, db.CounterWrites);
            Assert.Equal(0, db.SyncingTagQueries);
        }

        [Theory]
        [InlineData(false, DeleteCorpIdResult.Success, 6)]
        [InlineData(true, DeleteCorpIdResult.Success, 5)]
        [InlineData(true, DeleteCorpIdResult.Error, 6)]
        public async Task Run_WhenCanceledDuringAddition_FinishesDeviceAndCommitsNetAdditions(
            bool rollback, DeleteCorpIdResult rollbackResult, int expectedCount)
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            using var cts = new CancellationTokenSource();
            var firstDevice = new Device();
            var secondDevice = new Device();
            var db = new StubCosmosDbService
            {
                SyncingTagsToReturn = ["enabled-tag"],
                NotSyncingDevicesInTagsToReturn = [firstDevice, secondDevice],
                CounterToReturn = new CorpIDCounter(0) { CorpIDCount = 5, CorpIDReserve = 3 }
            };
            if (rollback)
            {
                db.OnUpdateDevice = _ => throw new Microsoft.Azure.Cosmos.CosmosException(
                    "not found", System.Net.HttpStatusCode.NotFound, 0, "test", 0);
            }
            var graph = new StubGraphBetaService
            {
                OnAdd = () => cts.Cancel(),
                OnDelete = _ => Task.FromResult(rollbackResult)
            };

            await CreateSut(new CapturingLoggerFactory(), db, graph: graph)
                .Run(new TimerInfo(), cts.Token);

            Assert.Equal(1, graph.AddCalls);
            Assert.Same(firstDevice, Assert.Single(db.UpdatedDevices));
            Assert.Empty(secondDevice.CorporateIdentityID);
            Assert.Equal(rollback ? 1 : 0, graph.DeleteCalls);
            Assert.Equal(expectedCount, db.CounterToReturn.CorpIDCount);
            Assert.Equal(3, db.CounterToReturn.CorpIDReserve);
            Assert.Equal(2, db.CounterWrites);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Run_WhenCanceledBeforeFirstAddition_DoesNotAddAndReleasesAnyReservation(bool afterReservation)
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            using var cts = new CancellationTokenSource();
            var db = new StubCosmosDbService
            {
                SyncingTagsToReturn = ["enabled-tag"],
                NotSyncingDevicesInTagsToReturn = [new Device()],
                CounterToReturn = new CorpIDCounter(0) { CorpIDCount = 5, CorpIDReserve = 3 }
            };
            if (afterReservation)
            {
                db.OnCounterWrite = () => cts.Cancel();
            }
            else
            {
                db.OnGetNotSyncingDevices = () => cts.Cancel();
            }
            var graph = new StubGraphBetaService();

            await CreateSut(new CapturingLoggerFactory(), db, graph: graph).Run(new TimerInfo(), cts.Token);

            Assert.Equal(0, graph.AddCalls);
            Assert.Empty(db.UpdatedDevices);
            Assert.Equal(5, db.CounterToReturn.CorpIDCount);
            Assert.Equal(3, db.CounterToReturn.CorpIDReserve);
            Assert.Equal(afterReservation ? 2 : 0, db.CounterWrites);
        }

        /// <summary>
        /// Verifies that Run completes a full execution including both the non-syncing and syncing device sections when sync is enabled.
        /// </summary>
        [Fact]
        public async Task Run_WhenSyncEnabled_CompletesFullRunWithBothSections()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var db = new StubCosmosDbService
            {
                NonSyncingTagsToReturn = [],
                SyncedDevicesInTagsToReturn = [],
                SyncingTagsToReturn = [],
            };

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory, db);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: false);

            await sut.Run(timer);

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Removed Corp IDs for"));

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Added Corp IDs for"));

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("ReconcileSyncState completed"));
        }

    }
}
