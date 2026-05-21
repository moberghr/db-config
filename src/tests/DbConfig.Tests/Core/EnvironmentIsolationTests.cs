using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Verifies that the Environment column acts as a hard scalar filter — rows from a
/// different environment are invisible to a polling provider configured for env A,
/// and cross-environment writes do not advance the host's watermark.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EnvironmentIsolationTests
{
    private const string OwnApp = "MyApp";
    private const string SharedApp = "Shared";
    private const string TenantAcme = "Acme";

    private static DbConfigConfigurationProvider CreateProvider(
        IConfigStore store,
        string environment,
        string[]? includeScopes = null,
        TimeProvider? timeProvider = null)
    {
        var options = new DbConfigOptions
        {
            Scope = OwnApp,
            Environment = environment,
            ReloadInterval = TimeSpan.FromSeconds(30),
            IncludeScopes = includeScopes ?? [],
        };

        return new DbConfigConfigurationProvider(
            options,
            store,
            timeProvider ?? TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    [TimedFact]
    public async Task GlobalRowInDifferentEnv_NotVisible()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(OwnApp, "Staging", string.Empty, "K", "staging-v", false, t, null), ct);

        var provider = CreateProvider(store, "Prod");
        provider.Load();

        provider.TryGet("K", out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [TimedFact]
    public async Task TenantRowInDifferentEnv_NotVisible()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(OwnApp, "Staging", TenantAcme, "K", "staging-acme", false, t, null), ct);

        var provider = CreateProvider(store, "Prod");
        provider.Load();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(new MutableTenantResolver { Tenant = TenantAcme });
        await using var sp = services.BuildServiceProvider();
        provider.HostServiceProvider = sp;

        provider.TryGet("K", out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [TimedFact]
    public async Task SameKeyDifferentEnvs_OnlyMatchingEnvReturned()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(OwnApp, "Prod", string.Empty, "K", "prod-v", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntryRecord(OwnApp, "Staging", string.Empty, "K", "staging-v", false, t, null), ct);

        var provider = CreateProvider(store, "Prod");
        provider.Load();

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("prod-v");
    }

    [TimedFact]
    public async Task CrossEnvWrite_DoesNotAdvanceWatermark()
    {
        var fakeTime = new FakeTimeProvider();
        var store = new InMemoryConfigStore();
        var t0 = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(OwnApp, "Prod", string.Empty, "K", "prod-v", false, t0, null), ct);

        var provider = CreateProvider(store, "Prod", timeProvider: fakeTime);
        provider.Load();

        var reloadFired = false;
        provider.GetReloadToken().RegisterChangeCallback(_ => reloadFired = true, null);

        // Write to a different environment with a much later timestamp.
        var t1 = t0.AddMinutes(10);
        await store.UpsertAsync(new ConfigEntryRecord(OwnApp, "Staging", string.Empty, "K", "staging-v", false, t1, null), ct);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Give the timer callback a brief window to run.
        await Task.Delay(50, ct);

        reloadFired.ShouldBeFalse();
        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("prod-v");
    }

    [TimedFact]
    public async Task IncludeScopes_OnlyMatchingEnv_IsHonored()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        // Shared scope has the same key in both envs.
        await store.UpsertAsync(new ConfigEntryRecord(SharedApp, "Prod", string.Empty, "K", "shared-prod", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntryRecord(SharedApp, "Staging", string.Empty, "K", "shared-staging", false, t, null), ct);

        var provider = CreateProvider(store, "Prod", includeScopes: [SharedApp]);
        provider.Load();

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("shared-prod");
    }
}
