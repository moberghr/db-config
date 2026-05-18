using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Verifies the polling provider's multi-tenant load path:
/// uses <see cref="IConfigStore.GetAllForAllTenantsAsync"/> instead of GetAllAsync,
/// uses <see cref="IConfigStore.GetLatestModifiedUtcAcrossAllTenantsAsync"/> for change
/// detection, and confirms that tenant-specific writes advance the watermark and trigger reload.
/// Also verifies the tenant-aware <c>TryGet</c> behavior via <see cref="ITenantResolver"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PollingProviderMultiTenantTests
{
    private const string App = "MultiTenantApp";
    private const string Env = "Test";

    private sealed class FakeResolver : ITenantResolver
    {
        public string? Tenant { get; set; }

        public string? Resolve() => Tenant;
    }

    private static (DbConfigConfigurationProvider Provider, FakeTimeProvider TimeProvider, TrackingStore Store) CreateSut(
        TimeSpan? reloadInterval = null)
    {
        var interval = reloadInterval ?? TimeSpan.FromSeconds(30);
        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = interval,
        };

        var inner = new InMemoryConfigStore();
        var tracking = new TrackingStore(inner);
        var fakeTime = new FakeTimeProvider();

        var provider = new DbConfigConfigurationProvider(
            options,
            tracking,
            fakeTime,
            NullLoggerFactory.Instance);

        return (provider, fakeTime, tracking);
    }

    private static ServiceProvider BuildServiceProvider(ITenantResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        return services.BuildServiceProvider();
    }

    [TimedFact]
    public async Task PollingProvider_LoadsAllTenants_ViaGetAllForAllTenantsAsync()
    {
        var (provider, _, tracking) = CreateSut();
        var now = DateTimeOffset.UtcNow;

        await tracking.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key1", "global", false, now, null), TestContext.Current.CancellationToken);
        await tracking.UpsertAsync(new ConfigEntry(App, Env, "Acme", "Key1", "acme", false, now.AddSeconds(1), null), TestContext.Current.CancellationToken);

        provider.Load();

        // B64: composed path always uses GetAllScopedForAllTenants (scope list defaults to [AppName]).
        tracking.GetAllScopedForAllTenantsCallCount.ShouldBeGreaterThan(0);

        // Without a resolver, TryGet returns the global entry.
        provider.TryGet("Key1", out var globalValue).ShouldBeTrue();
        globalValue.ShouldBe("global");

        var resolver = new FakeResolver { Tenant = "Acme" };
        await using var sp = BuildServiceProvider(resolver);
        provider.HostServiceProvider = sp;

        provider.TryGet("Key1", out var acmeValue).ShouldBeTrue();
        acmeValue.ShouldBe("acme");
    }

    [TimedFact]
    public async Task PollingTick_TenantSpecificWriteAdvancesWatermark_Reloads()
    {
        var (provider, fakeTime, tracking) = CreateSut(TimeSpan.FromSeconds(30));
        var t0 = DateTimeOffset.UtcNow;

        await tracking.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Global:Key", "global-val", false, t0, null), TestContext.Current.CancellationToken);
        provider.Load();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        var t1 = t0.AddSeconds(1);
        await tracking.UpsertAsync(new ConfigEntry(App, Env, "Acme", "Acme:Key", "acme-val", false, t1, null), TestContext.Current.CancellationToken);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var resolver = new FakeResolver { Tenant = "Acme" };
        await using var sp = BuildServiceProvider(resolver);
        provider.HostServiceProvider = sp;

        provider.TryGet("Acme:Key", out var value).ShouldBeTrue();
        value.ShouldBe("acme-val");
    }

    [TimedFact]
    public async Task PollingProvider_NoResolver_GlobalEntryAccessibleViaBaseApi()
    {
        var (provider, _, tracking) = CreateSut();
        var now = DateTimeOffset.UtcNow;

        await tracking.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "App:Version", "1.0", false, now, null), TestContext.Current.CancellationToken);
        await tracking.UpsertAsync(new ConfigEntry(App, Env, "Acme", "App:Version", "2.0", false, now.AddSeconds(1), null), TestContext.Current.CancellationToken);

        provider.Load();

        // No resolver set — TryGet falls back to global entry.
        provider.TryGet("App:Version", out var baseValue).ShouldBeTrue();
        baseValue.ShouldBe("1.0");
    }

    [TimedFact]
    public async Task TryGet_ResolverReturnsTenant_ReturnsTenantEntry()
    {
        var options = new DbConfigOptions { AppName = App, Environment = Env, ReloadInterval = TimeSpan.FromSeconds(30) };
        var store = new InMemoryConfigStore();
        var realProvider = new DbConfigConfigurationProvider(options, store, TimeProvider.System, NullLoggerFactory.Instance);

        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:ApiKey", "global-key", false, now, null), TestContext.Current.CancellationToken);
        await store.UpsertAsync(new ConfigEntry(App, Env, "Acme", "Stripe:ApiKey", "acme-key", false, now.AddSeconds(1), null), TestContext.Current.CancellationToken);

        realProvider.Load();

        var resolver = new FakeResolver { Tenant = "Acme" };
        await using var sp = BuildServiceProvider(resolver);
        realProvider.HostServiceProvider = sp;

        realProvider.TryGet("Stripe:ApiKey", out var value).ShouldBeTrue();
        value.ShouldBe("acme-key");
    }

    [TimedFact]
    public async Task TryGet_ResolverReturnsTenant_MissingTenantEntry_FallsBackToGlobal()
    {
        var options = new DbConfigOptions { AppName = App, Environment = Env, ReloadInterval = TimeSpan.FromSeconds(30) };
        var store = new InMemoryConfigStore();
        var realProvider = new DbConfigConfigurationProvider(options, store, TimeProvider.System, NullLoggerFactory.Instance);

        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Feature:Flag", "true", false, now, null), TestContext.Current.CancellationToken);

        // Globex has NO override for Feature:Flag
        realProvider.Load();

        var resolver = new FakeResolver { Tenant = "Globex" };
        await using var sp = BuildServiceProvider(resolver);
        realProvider.HostServiceProvider = sp;

        realProvider.TryGet("Feature:Flag", out var value).ShouldBeTrue();
        value.ShouldBe("true");
    }

    private sealed class TrackingStore : IConfigStore
    {
        private readonly IConfigStore _inner;

        public int GetAllForAllTenantsCallCount { get; private set; }

        public int GetAllScopedForAllTenantsCallCount { get; private set; }

        public TrackingStore(IConfigStore inner) => _inner = inner;

        public Task<IReadOnlyList<ConfigEntry>> GetAllAsync(string appName, string environment, CancellationToken ct)
            => _inner.GetAllAsync(appName, environment, ct);

        public Task<ConfigEntry?> GetAsync(string appName, string environment, string key, CancellationToken ct)
            => _inner.GetAsync(appName, environment, key, ct);

        public Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string appName, string environment, CancellationToken ct)
            => _inner.GetLatestModifiedUtcAsync(appName, environment, ct);

        public Task UpsertAsync(ConfigEntry entry, CancellationToken ct)
            => _inner.UpsertAsync(entry, ct);

        public Task DeleteAsync(string appName, string environment, string key, CancellationToken ct)
            => _inner.DeleteAsync(appName, environment, key, ct);

        public Task<IReadOnlyList<ConfigEntry>> GetAllScopedAsync(
            IReadOnlyList<string> appNames, string environment, CancellationToken ct)
            => _inner.GetAllScopedAsync(appNames, environment, ct);

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

        public Task<IReadOnlyList<ConfigEntry>> GetAllForAllTenantsAsync(
            string appName, string environment, CancellationToken ct)
        {
            GetAllForAllTenantsCallCount++;
            return _inner.GetAllForAllTenantsAsync(appName, environment, ct);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
            string appName, string environment, CancellationToken ct)
            => _inner.GetLatestModifiedUtcAcrossAllTenantsAsync(appName, environment, ct);

        public Task<IReadOnlyList<ConfigEntry>> GetAllScopedForAllTenantsAsync(
            IReadOnlyList<string> appNames, string environment, CancellationToken ct)
        {
            GetAllScopedForAllTenantsCallCount++;
            return _inner.GetAllScopedForAllTenantsAsync(appNames, environment, ct);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
            IReadOnlyList<string> appNames, string environment, CancellationToken ct)
            => _inner.GetLatestModifiedUtcScopedAcrossAllTenantsAsync(appNames, environment, ct);

        public Task<IReadOnlyList<ConfigEntry>> QueryAsync(
            string? appName, string? environment, string? tenantId, string? keyPrefix, int take, CancellationToken ct)
            => _inner.QueryAsync(appName, environment, tenantId, keyPrefix, take, ct);
    }
}
