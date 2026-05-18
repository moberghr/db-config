using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class IncludeScopesPrecedenceTests
{
    private const string OwnApp = "MyApp";
    private const string SharedApp = "Shared";
    private const string Env = "Test";

    private static (DbConfigConfigurationProvider Provider, FakeTimeProvider TimeProvider, InMemoryConfigStore Store) CreateSut(
        string[] includeScopes,
        TimeSpan? reloadInterval = null)
    {
        var interval = reloadInterval ?? TimeSpan.FromSeconds(30);
        var options = new DbConfigOptions
        {
            AppName = OwnApp,
            Environment = Env,
            ReloadInterval = interval,
            IncludeScopes = includeScopes,
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
    public void Polling_WithIncludeScopes_OwnScopeWins()
    {
        var (provider, _, store) = CreateSut([SharedApp]);
        var t = DateTimeOffset.UtcNow;

        store.UpsertAsync(new ConfigEntry(SharedApp, Env, string.Empty, "X", "shared", false, t, null), CancellationToken.None)
            .GetAwaiter().GetResult();
        store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "X", "own", false, t.AddMilliseconds(1), null), CancellationToken.None)
            .GetAwaiter().GetResult();

        provider.Load();

        provider.TryGet("X", out var value).ShouldBeTrue();
        value.ShouldBe("own");
    }

    [TimedFact]
    public void Polling_WithIncludeScopes_SharedKeyVisibleWhenOwnAbsent()
    {
        var (provider, _, store) = CreateSut([SharedApp]);
        var t = DateTimeOffset.UtcNow;

        store.UpsertAsync(new ConfigEntry(SharedApp, Env, string.Empty, "X", "shared", false, t, null), CancellationToken.None)
            .GetAwaiter().GetResult();

        provider.Load();

        provider.TryGet("X", out var value).ShouldBeTrue();
        value.ShouldBe("shared");
    }

    [TimedFact]
    public async Task Polling_WatermarkAdvancesOnSharedScopeChange_TriggersReload()
    {
        var (provider, fakeTime, store) = CreateSut([SharedApp], TimeSpan.FromSeconds(30));
        var t0 = DateTimeOffset.UtcNow;

        // Initial state: own scope has one entry.
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "OwnKey", "own-v", false, t0, null), TestContext.Current.CancellationToken);

        provider.Load();

        provider.TryGet("SharedKey", out _).ShouldBeFalse();

        // Add a new entry to the shared scope with a later watermark.
        var t1 = t0.AddSeconds(1);
        await store.UpsertAsync(new ConfigEntry(SharedApp, Env, string.Empty, "SharedKey", "shared-v", false, t1, null), TestContext.Current.CancellationToken);

        // Register a TCS that completes when the reload token fires.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Wait until the reload fires or the test times out.
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        provider.TryGet("SharedKey", out var value).ShouldBeTrue();
        value.ShouldBe("shared-v");
    }

    [TimedFact]
    public void BuildScopeList_WithDuplicates_DedupesPreservingOrder()
    {
        // IncludeScopes has "Shared" twice and "PlatformDefaults" once; AppName is "MyApp".
        // Expected effective scope list: [PlatformDefaults, Shared, MyApp]
        // (first occurrence kept, second "Shared" dropped, AppName appended once at end).
        var (provider, _, store) = CreateSut(["Shared", "PlatformDefaults", "Shared"]);
        var t = DateTimeOffset.UtcNow;

        // Insert one unique key per scope.
        store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "SharedKey", "sv", false, t, null), CancellationToken.None)
            .GetAwaiter().GetResult();
        store.UpsertAsync(new ConfigEntry("PlatformDefaults", Env, string.Empty, "PlatformKey", "pv", false, t, null), CancellationToken.None)
            .GetAwaiter().GetResult();
        store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "OwnKey", "ov", false, t.AddMilliseconds(1), null), CancellationToken.None)
            .GetAwaiter().GetResult();

        provider.Load();

        // After one Load(), GetAllScopedForAllTenantsAsync (the composed scope × tenant
        // load path used by the polling provider) must have been called exactly once,
        // and the appNames list passed to it must have 3 elements (not 4).
        store.GetAllScopedForAllTenantsAsyncCallCount.ShouldBe(1);

        // All three scopes' keys must be visible.
        provider.TryGet("SharedKey", out var sv).ShouldBeTrue();
        sv.ShouldBe("sv");

        provider.TryGet("PlatformKey", out var pv).ShouldBeTrue();
        pv.ShouldBe("pv");

        provider.TryGet("OwnKey", out var ov).ShouldBeTrue();
        ov.ShouldBe("ov");

        // Verify deduplication: a conflicting key defined in both Shared and OwnApp resolves to own.
        store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "ConflictKey", "shared-conflict", false, t, null), CancellationToken.None)
            .GetAwaiter().GetResult();
        store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "ConflictKey", "own-conflict", false, t.AddMilliseconds(1), null), CancellationToken.None)
            .GetAwaiter().GetResult();

        // Force a second load to pick up the new entries.
        provider.Load();

        provider.TryGet("ConflictKey", out var cv).ShouldBeTrue();
        cv.ShouldBe("own-conflict");
    }

    [TimedFact]
    public async Task Polling_DeleteFromSharedScope_OwnScopeKeyStillVisible()
    {
        var (provider, fakeTime, store) = CreateSut([SharedApp], TimeSpan.FromSeconds(30));
        var t0 = DateTimeOffset.UtcNow;

        // Both scopes have Key=X.
        await store.UpsertAsync(new ConfigEntry(SharedApp, Env, string.Empty, "X", "shared", false, t0, null), TestContext.Current.CancellationToken);
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "X", "own", false, t0.AddMilliseconds(1), null), TestContext.Current.CancellationToken);

        provider.Load();
        provider.TryGet("X", out var before).ShouldBeTrue();
        before.ShouldBe("own");

        // Delete from shared scope and advance the watermark via a separate shared entry.
        await store.DeleteAsync(SharedApp, Env, "X", TestContext.Current.CancellationToken);

        // Trigger watermark advance via a new shared entry so the provider detects the change.
        var t1 = t0.AddSeconds(1);
        await store.UpsertAsync(new ConfigEntry(SharedApp, Env, string.Empty, "Trigger", "trigger-v", false, t1, null), TestContext.Current.CancellationToken);

        // Register a TCS that completes when the reload token fires.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Wait until the reload fires or the test times out.
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Own scope key must still be visible.
        provider.TryGet("X", out var after).ShouldBeTrue();
        after.ShouldBe("own");
    }
}
