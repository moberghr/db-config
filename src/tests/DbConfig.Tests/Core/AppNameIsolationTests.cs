using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Verifies that rows belonging to an AppName outside the configured scope set
/// (own AppName + IncludeScopes) are invisible to the polling provider, regardless of
/// tenant id.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AppNameIsolationTests
{
    private const string OwnApp = "MyApp";
    private const string SharedApp = "Shared";
    private const string TenantAcme = "Acme";
    private const string Env = "Prod";

    private static DbConfigConfigurationProvider CreateProvider(
        IConfigStore store,
        string[]? includeScopes = null)
    {
        var options = new DbConfigOptions
        {
            AppName = OwnApp,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
            IncludeScopes = includeScopes ?? [],
        };

        return new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    [TimedFact]
    public async Task UnrelatedAppName_NotInScopes_NotVisible()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry("OtherApp", Env, string.Empty, "K", "other-v", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        provider.TryGet("K", out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [TimedFact]
    public async Task UnrelatedAppName_TenantRow_NotVisible()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry("OtherApp", Env, TenantAcme, "K", "other-acme", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(new MutableTenantResolver { Tenant = TenantAcme });
        await using var sp = services.BuildServiceProvider();
        provider.HostServiceProvider = sp;

        provider.TryGet("K", out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [TimedFact]
    public async Task AppOutsideIncludeScopes_NotVisible()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(SharedApp, Env, string.Empty, "SharedK", "sv", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry("Foreign", Env, string.Empty, "ForeignK", "fv", false, t, null), ct);

        // IncludeScopes lists Shared but NOT Foreign.
        var provider = CreateProvider(store, [SharedApp]);
        provider.Load();

        provider.TryGet("SharedK", out var sharedValue).ShouldBeTrue();
        sharedValue.ShouldBe("sv");

        provider.TryGet("ForeignK", out var foreignValue).ShouldBeFalse();
        foreignValue.ShouldBeNull();
    }

    [TimedFact]
    public async Task OwnApp_SeparatesFromOtherAppRowsWithMatchingKey()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "Same:Key", "own-v", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry("OtherApp", Env, string.Empty, "Same:Key", "other-v", false, t.AddSeconds(1), null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        provider.TryGet("Same:Key", out var value).ShouldBeTrue();
        value.ShouldBe("own-v");
    }

    [TimedFact]
    public async Task UnrelatedAppName_DoesNotContributeToTenantBag()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        // Own-app tenant entry exists.
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, TenantAcme, "K", "own-acme", false, t, null), ct);

        // Different app has a different value for the same (tenant, key).
        await store.UpsertAsync(new ConfigEntry("OtherApp", Env, TenantAcme, "K", "other-acme", false, t.AddSeconds(1), null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(new MutableTenantResolver { Tenant = TenantAcme });
        await using var sp = services.BuildServiceProvider();
        provider.HostServiceProvider = sp;

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("own-acme");
    }
}
