using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Verifies that with 3+ IncludeScopes the precedence chain
/// <c>[Lower, Mid, Higher] + Scope</c> is stable: the lowest is overridden by the middle,
/// the middle is overridden by the higher, and all are overridden by Scope.
/// Also verifies the same precedence applies within a tenant bag.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MultipleIncludeScopesPrecedenceTests
{
    private const string OwnApp = "MyApp";
    private const string Lower = "OrgGlobals";
    private const string Mid = "PlatformDefaults";
    private const string Higher = "Shared";
    private const string Env = "Prod";
    private const string TenantAcme = "Acme";

    private static DbConfigConfigurationProvider CreateProvider(IConfigStore store)
    {
        var options = new DbConfigOptions
        {
            Scope = OwnApp,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
            IncludeScopes = [Lower, Mid, Higher],
        };

        return new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    [TimedFact]
    public async Task LowerScope_OverriddenByMiddleScope()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(Lower, Env, string.Empty, "K", "lower", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(Mid, Env, string.Empty, "K", "mid", false, t.AddMilliseconds(1), null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("mid");
    }

    [TimedFact]
    public async Task MiddleScope_OverriddenByHigherScope()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(Mid, Env, string.Empty, "K", "mid", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(Higher, Env, string.Empty, "K", "higher", false, t.AddMilliseconds(1), null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("higher");
    }

    [TimedFact]
    public async Task OwnApp_OverridesAllIncludeScopes()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(Lower, Env, string.Empty, "K", "lower", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(Mid, Env, string.Empty, "K", "mid", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(Higher, Env, string.Empty, "K", "higher", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "own", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("own");
    }

    [TimedFact]
    public async Task EachScopesUniqueKey_AllVisible()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(Lower, Env, string.Empty, "LowerKey", "lv", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(Mid, Env, string.Empty, "MidKey", "mv", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(Higher, Env, string.Empty, "HigherKey", "hv", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "OwnKey", "ov", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        provider.TryGet("LowerKey", out var lv).ShouldBeTrue();
        lv.ShouldBe("lv");

        provider.TryGet("MidKey", out var mv).ShouldBeTrue();
        mv.ShouldBe("mv");

        provider.TryGet("HigherKey", out var hv).ShouldBeTrue();
        hv.ShouldBe("hv");

        provider.TryGet("OwnKey", out var ov).ShouldBeTrue();
        ov.ShouldBe("ov");
    }

    [TimedFact]
    public async Task PrecedenceApplies_WithinTenantBag()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(Lower, Env, TenantAcme, "K", "acme-lower", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(Mid, Env, TenantAcme, "K", "acme-mid", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(Higher, Env, TenantAcme, "K", "acme-higher", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(new MutableTenantResolver { Tenant = TenantAcme });
        await using var sp = services.BuildServiceProvider();
        provider.HostServiceProvider = sp;

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("acme-higher");
    }
}
