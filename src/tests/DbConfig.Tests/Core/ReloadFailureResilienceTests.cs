using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class ReloadFailureResilienceTests
{
    private const string App = "TestApp";
    private const string Env = "Test";

    [TimedFact]
    public void Load_WhenStoreSynchronouslyThrows_ThrowsInvalidOperationException()
    {
        var mockStore = new Mock<IConfigStore>();
        mockStore
            .Setup(x => x.GetAllAsync(App, Env, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Store unavailable"));

        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
        };

        var provider = new DbConfigConfigurationProvider(options, mockStore.Object, TimeProvider.System, NullLoggerFactory.Instance);

        Should.Throw<InvalidOperationException>(() => provider.Load())
            .Message.ShouldContain("DbConfig failed to load configuration on startup");
    }

    [TimedFact]
    public async Task ReloadTick_WhenStoreSynchronouslyThrows_PreviousValuesRetained()
    {
        var store = new InMemoryConfigStore();
        var fakeTime = new FakeTimeProvider();

        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
        };

        var t0 = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key", "original", false, t0, null), TestContext.Current.CancellationToken);

        var faultyStore = new FaultyWrapperStore(store, faultOnNextCall: false);
        var provider = new DbConfigConfigurationProvider(options, faultyStore, fakeTime, NullLoggerFactory.Instance);
        provider.Load();

        provider.TryGet("Key", out var beforeFault).ShouldBeTrue();
        beforeFault.ShouldBe("original");

        // Configure the wrapper to throw on GetAllAsync BEFORE advancing the watermark to
        // eliminate the race where the upsert tick completes normally before the fault is set.
        faultyStore.FaultOnGetAll = true;

        // Update watermark so the provider thinks a reload is needed.
        var t1 = t0.AddSeconds(1);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key", "shouldNotSee", false, t1, null), TestContext.Current.CancellationToken);

        // Arm the signal before advancing time so the next GetAll call completes the TCS.
        faultyStore.ArmNextGetAllSignal();

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Wait until the timer callback has attempted (and failed) GetAllAsync.
        await faultyStore.NextGetAllSignal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Previous values must still be intact.
        provider.TryGet("Key", out var afterFault).ShouldBeTrue();
        afterFault.ShouldBe("original");
    }

    [TimedFact]
    public async Task ReloadTick_AfterFailure_RecoveryOnNextSuccessfulTick()
    {
        var store = new InMemoryConfigStore();
        var fakeTime = new FakeTimeProvider();

        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
        };

        var t0 = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key", "original", false, t0, null), TestContext.Current.CancellationToken);

        var faultyStore = new FaultyWrapperStore(store, faultOnNextCall: false);
        var provider = new DbConfigConfigurationProvider(options, faultyStore, fakeTime, NullLoggerFactory.Instance);
        provider.Load();

        // First reload tick: watermark advances and GetAll faults.
        var t1 = t0.AddSeconds(1);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key", "recovered", false, t1, null), TestContext.Current.CancellationToken);
        faultyStore.FaultOnGetAll = true;
        faultyStore.ArmNextGetAllSignal();
        fakeTime.Advance(TimeSpan.FromSeconds(30));
        await faultyStore.NextGetAllSignal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Second reload tick: fault cleared, successful reload.
        faultyStore.FaultOnGetAll = false;

        // Register a TCS that completes when the reload token fires after recovery.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Wait until the reload completes successfully.
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        provider.TryGet("Key", out var afterRecovery).ShouldBeTrue();
        afterRecovery.ShouldBe("recovered");
    }

    /// <summary>
    /// Wraps an <see cref="InMemoryConfigStore"/> and can be configured to throw on
    /// <see cref="GetAllAsync"/> to simulate a transient store fault during reload.
    /// Call <see cref="ArmNextGetAllSignal"/> before advancing time; then
    /// <c>await NextGetAllSignal.WaitAsync(...)</c> to block until the timer callback
    /// has invoked GetAllAsync.
    /// </summary>
    private sealed class FaultyWrapperStore : IConfigStore
    {
        private readonly IConfigStore _inner;
        private TaskCompletionSource<bool>? _nextGetAllTcs;

        public bool FaultOnGetAll { get; set; }

        /// <summary>The task that completes after the next GetAllAsync call.</summary>
        public Task NextGetAllSignal => _nextGetAllTcs?.Task ?? Task.CompletedTask;

        /// <summary>Arms the signal so that the next GetAllAsync call completes <see cref="NextGetAllSignal"/>.</summary>
        public void ArmNextGetAllSignal()
        {
            _nextGetAllTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public FaultyWrapperStore(IConfigStore inner, bool faultOnNextCall)
        {
            _inner = inner;
            FaultOnGetAll = faultOnNextCall;
        }

        public async Task<IReadOnlyList<ConfigEntry>> GetAllAsync(string appName, string environment, CancellationToken ct)
        {
            _nextGetAllTcs?.TrySetResult(true);

            if (FaultOnGetAll)
            {
                throw new TimeoutException("Simulated store fault");
            }

            return await _inner.GetAllAsync(appName, environment, ct);
        }

        public Task<ConfigEntry?> GetAsync(string appName, string environment, string key, CancellationToken ct)
            => _inner.GetAsync(appName, environment, key, ct);

        public Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string appName, string environment, CancellationToken ct)
        {
            return _inner.GetLatestModifiedUtcAsync(appName, environment, ct);
        }

        public Task UpsertAsync(ConfigEntry entry, CancellationToken ct)
        {
            return _inner.UpsertAsync(entry, ct);
        }

        public Task DeleteAsync(string appName, string environment, string key, CancellationToken ct)
        {
            return _inner.DeleteAsync(appName, environment, key, ct);
        }

        public async Task<IReadOnlyList<ConfigEntry>> GetAllScopedAsync(
            IReadOnlyList<string> appNames, string environment, CancellationToken ct)
        {
            _nextGetAllTcs?.TrySetResult(true);

            if (FaultOnGetAll)
            {
                throw new TimeoutException("Simulated store fault");
            }

            return await _inner.GetAllScopedAsync(appNames, environment, ct);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
            IReadOnlyList<string> appNames, string environment, CancellationToken ct)
            => _inner.GetLatestModifiedUtcScopedAsync(appNames, environment, ct);

        public Task<IReadOnlyList<ConfigEntry>> GetAllForTenantAsync(
            string appName, string environment, string tenantId, CancellationToken ct)
            => _inner.GetAllForTenantAsync(appName, environment, tenantId, ct);

        public Task<ConfigEntry?> GetForTenantAsync(
            string appName, string environment, string tenantId, string key, CancellationToken ct)
            => _inner.GetForTenantAsync(appName, environment, tenantId, key, ct);

        public Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
            string appName, string environment, string tenantId, CancellationToken ct)
            => _inner.GetLatestModifiedUtcForTenantAsync(appName, environment, tenantId, ct);

        public Task DeleteForTenantAsync(
            string appName, string environment, string tenantId, string key, CancellationToken ct)
            => _inner.DeleteForTenantAsync(appName, environment, tenantId, key, ct);

        public async Task<IReadOnlyList<ConfigEntry>> GetAllForAllTenantsAsync(
            string appName, string environment, CancellationToken ct)
        {
            _nextGetAllTcs?.TrySetResult(true);

            if (FaultOnGetAll)
            {
                throw new TimeoutException("Simulated store fault");
            }

            return await _inner.GetAllForAllTenantsAsync(appName, environment, ct);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
            string appName, string environment, CancellationToken ct)
            => _inner.GetLatestModifiedUtcAcrossAllTenantsAsync(appName, environment, ct);

        public async Task<IReadOnlyList<ConfigEntry>> GetAllScopedForAllTenantsAsync(
            IReadOnlyList<string> appNames, string environment, CancellationToken ct)
        {
            _nextGetAllTcs?.TrySetResult(true);

            if (FaultOnGetAll)
            {
                throw new TimeoutException("Simulated store fault");
            }

            return await _inner.GetAllScopedForAllTenantsAsync(appNames, environment, ct);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
            IReadOnlyList<string> appNames, string environment, CancellationToken ct)
            => _inner.GetLatestModifiedUtcScopedAcrossAllTenantsAsync(appNames, environment, ct);
    }
}
