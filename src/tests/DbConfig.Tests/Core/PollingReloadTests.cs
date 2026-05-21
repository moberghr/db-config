using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class PollingReloadTests
{
    private const string App = "TestApp";
    private const string Env = "Test";

    private static (DbConfigConfigurationProvider Provider, FakeTimeProvider TimeProvider, InMemoryConfigStore Store) CreateSut(
        TimeSpan? reloadInterval = null)
    {
        var interval = reloadInterval ?? TimeSpan.FromSeconds(30);
        var options = new DbConfigOptions
        {
            Scope = App,
            Environment = Env,
            ReloadInterval = interval,
        };

        var store = new InMemoryConfigStore();
        var fakeTime = new FakeTimeProvider();

        var provider = new DbConfigConfigurationProvider(
            options,
            store,
            fakeTime,
            NullLoggerFactory.Instance);

        return (provider, fakeTime, store);
    }

    [TimedFact]
    public void Load_NoEntries_ReturnsEmpty()
    {
        var (provider, _, _) = CreateSut();
        provider.Load();

        provider.TryGet("AnyKey", out _).ShouldBeFalse();
    }

    [TimedFact]
    public void Load_WithEntries_ReturnsValues()
    {
        var (provider, _, store) = CreateSut();
        var now = DateTimeOffset.UtcNow;
        store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "val1", false, now, null), CancellationToken.None)
            .GetAwaiter().GetResult();

        provider.Load();

        provider.TryGet("Key1", out var value).ShouldBeTrue();
        value.ShouldBe("val1");
    }

    [TimedFact]
    public async Task AfterInterval_NewEntryAdded_ProviderReloads()
    {
        var (provider, fakeTime, store) = CreateSut(TimeSpan.FromSeconds(30));
        var t0 = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "initial", false, t0, null), TestContext.Current.CancellationToken);

        provider.Load();

        provider.TryGet("Key1", out var before).ShouldBeTrue();
        before.ShouldBe("initial");

        // Add a new entry with a later watermark.
        var t1 = t0.AddSeconds(1);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key2", "added", false, t1, null), TestContext.Current.CancellationToken);

        // Register a TCS that completes when the reload token fires.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        // Advance the fake timer by one full interval to trigger the timer callback.
        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Wait until the reload fires or the test times out.
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        provider.TryGet("Key2", out var after).ShouldBeTrue();
        after.ShouldBe("added");
    }

    [TimedFact]
    public async Task AfterInterval_NoNewEntries_ProviderDoesNotFireReload()
    {
        // Wrap the store with a signalling decorator so we can deterministically wait for the
        // timer callback to run (via GetLatestModifiedUtcAsync completing) rather than polling.
        var interval = TimeSpan.FromSeconds(30);
        var options = new DbConfigOptions
        {
            Scope = App,
            Environment = Env,
            ReloadInterval = interval,
        };

        var store = new InMemoryConfigStore();
        var fakeTime = new FakeTimeProvider();
        var countingStore = new CountingWatermarkStore(store);

        var provider = new DbConfigConfigurationProvider(
            options,
            countingStore,
            fakeTime,
            NullLoggerFactory.Instance);

        var t0 = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "value", false, t0, null), TestContext.Current.CancellationToken);

        provider.Load();

        // Record the watermark call count after initial Load (may include one call during Load).
        var callsAfterLoad = countingStore.WatermarkCallCount;

        var reloadFired = false;
        var token = provider.GetReloadToken();
        token.RegisterChangeCallback(_ => reloadFired = true, null);

        // Arm a TCS that fires the next time the timer callback calls GetLatestModifiedUtcAsync.
        countingStore.ArmNextCallSignal();

        // Advance past one interval — watermark unchanged so no reload should fire.
        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Wait until the timer callback has run at least once (watermark count advances).
        await countingStore.NextCallSignal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        _ = callsAfterLoad; // consumed above; kept for clarity
        reloadFired.ShouldBeFalse();
    }

    /// <summary>
    /// Wraps an <see cref="InMemoryConfigStore"/> and counts calls to
    /// <see cref="GetLatestModifiedUtcAsync"/> so tests can deterministically detect
    /// when the timer callback has run.  Call <see cref="ArmNextCallSignal"/> before
    /// advancing time; then <c>await NextCallSignal.WaitAsync(...)</c> to block until
    /// the timer callback has completed its watermark check.
    /// </summary>
    private sealed class CountingWatermarkStore : IConfigStore
    {
        private readonly IConfigStore _inner;
        private int _watermarkCallCount;
        private TaskCompletionSource<bool>? _nextCallTcs;

        public int WatermarkCallCount => _watermarkCallCount;

        /// <summary>The task that completes after the next watermark call.</summary>
        public Task NextCallSignal => _nextCallTcs?.Task ?? Task.CompletedTask;

        /// <summary>Arms the signal so that the next watermark call completes <see cref="NextCallSignal"/>.</summary>
        public void ArmNextCallSignal()
        {
            _nextCallTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public CountingWatermarkStore(IConfigStore inner)
        {
            _inner = inner;
        }

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(string scope, string environment, CancellationToken ct)
            => _inner.GetAllAsync(scope, environment, ct);

        public Task<ConfigEntryRecord?> GetAsync(string scope, string environment, string key, CancellationToken ct)
            => _inner.GetAsync(scope, environment, key, ct);

        public Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string scope, string environment, CancellationToken ct)
        {
            Interlocked.Increment(ref _watermarkCallCount);
            var result = _inner.GetLatestModifiedUtcAsync(scope, environment, ct);
            _nextCallTcs?.TrySetResult(true);
            return result;
        }

        public Task UpsertAsync(ConfigEntryRecord entry, CancellationToken ct)
            => _inner.UpsertAsync(entry, ct);

        public Task DeleteAsync(string scope, string environment, string key, CancellationToken ct)
            => _inner.DeleteAsync(scope, environment, key, ct);

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedAsync(
            IReadOnlyList<string> scopes, string environment, CancellationToken ct)
            => _inner.GetAllScopedAsync(scopes, environment, ct);

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
            IReadOnlyList<string> scopes, string environment, CancellationToken ct)
        {
            Interlocked.Increment(ref _watermarkCallCount);
            var result = _inner.GetLatestModifiedUtcScopedAsync(scopes, environment, ct);
            _nextCallTcs?.TrySetResult(true);
            return result;
        }

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(
            string scope, string environment, string tenantId, CancellationToken ct)
            => _inner.GetAllForTenantAsync(scope, environment, tenantId, ct);

        public Task<ConfigEntryRecord?> GetForTenantAsync(
            string scope, string environment, string tenantId, string key, CancellationToken ct)
            => _inner.GetForTenantAsync(scope, environment, tenantId, key, ct);

        public Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
            string scope, string environment, string tenantId, CancellationToken ct)
            => _inner.GetLatestModifiedUtcForTenantAsync(scope, environment, tenantId, ct);

        public Task DeleteForTenantAsync(
            string scope, string environment, string tenantId, string key, CancellationToken ct)
            => _inner.DeleteForTenantAsync(scope, environment, tenantId, key, ct);

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForAllTenantsAsync(
            string scope, string environment, CancellationToken ct)
            => _inner.GetAllForAllTenantsAsync(scope, environment, ct);

        public Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
            string scope, string environment, CancellationToken ct)
        {
            Interlocked.Increment(ref _watermarkCallCount);
            var result = _inner.GetLatestModifiedUtcAcrossAllTenantsAsync(scope, environment, ct);
            _nextCallTcs?.TrySetResult(true);
            return result;
        }

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedForAllTenantsAsync(
            IReadOnlyList<string> scopes, string environment, CancellationToken ct)
            => _inner.GetAllScopedForAllTenantsAsync(scopes, environment, ct);

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(CancellationToken ct)
            => _inner.GetAllAsync(ct);

        public Task<ConfigEntryRecord?> GetAsync(string key, CancellationToken ct)
            => _inner.GetAsync(key, ct);

        public Task<T> GetAsync<T>(CancellationToken ct)
            where T : class, new()
            => _inner.GetAsync<T>(ct);

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(string tenantId, CancellationToken ct)
            => _inner.GetAllForTenantAsync(tenantId, ct);

        public Task<ConfigEntryRecord?> GetForTenantAsync(string tenantId, string key, CancellationToken ct)
            => _inner.GetForTenantAsync(tenantId, key, ct);

        public Task<T> GetForTenantAsync<T>(string tenantId, CancellationToken ct)
            where T : class, new()
            => _inner.GetForTenantAsync<T>(tenantId, ct);

        public Task<IReadOnlyList<ConfigEntryRecord>> QueryAsync(
            string? scope, string? environment, string? tenantId, string? keyPrefix, int take, CancellationToken ct)
            => _inner.QueryAsync(scope, environment, tenantId, keyPrefix, take, ct);

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
            IReadOnlyList<string> scopes, string environment, CancellationToken ct)
        {
            Interlocked.Increment(ref _watermarkCallCount);
            var result = _inner.GetLatestModifiedUtcScopedAcrossAllTenantsAsync(scopes, environment, ct);
            _nextCallTcs?.TrySetResult(true);
            return result;
        }
    }

    [TimedFact]
    public async Task AfterInterval_ExistingEntryUpdated_ProviderReloads()
    {
        var (provider, fakeTime, store) = CreateSut(TimeSpan.FromSeconds(30));
        var t0 = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "old", false, t0, null), TestContext.Current.CancellationToken);

        provider.Load();

        provider.TryGet("Key1", out var before).ShouldBeTrue();
        before.ShouldBe("old");

        var t1 = t0.AddSeconds(1);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "new", false, t1, null), TestContext.Current.CancellationToken);

        // Register a TCS that completes when the reload token fires.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Wait until the reload fires or the test times out.
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        provider.TryGet("Key1", out var after).ShouldBeTrue();
        after.ShouldBe("new");
    }
}
