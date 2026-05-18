using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.PostgreSql;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// Integration tests for the composed scope × tenant store methods (B64) on PostgreSQL.
/// Verifies that <see cref="IConfigStore.GetAllScopedForAllTenantsAsync"/> and
/// <see cref="IConfigStore.GetLatestModifiedUtcScopedAcrossAllTenantsAsync"/> read across
/// every (scope, tenant) combination in a single round trip.
/// </summary>
[Trait("Category", "PostgreSql")]
[Collection(PostgreSqlFixture.CollectionName)]
public sealed class PostgreSqlStoreScopedTenantTests : IAsyncLifetime
{
    private const string Env = "Production";
    private const string AppOwn = "PaymentService";
    private const string AppShared = "Shared";
    private const string TenantAcme = "Acme";

    private readonly PostgreSqlFixture _fixture;
    private EfCoreConfigStore _store = null!;

    public PostgreSqlStoreScopedTenantTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
        _store = new EfCoreConfigStore(_fixture.DbContextFactory, new PostgreSqlUniqueConstraintDetector(), TimeProvider.System);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(30_000)]
    public async Task GetAllScopedForAllTenants_ReturnsRowsAcrossScopesAndTenants()
    {
        var t = DateTimeOffset.UtcNow;

        // Four rows: two scopes × two tenants (global "" + Acme).
        await _store.UpsertAsync(new ConfigEntry(AppOwn, Env, string.Empty, "K", "own-global", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(AppShared, Env, string.Empty, "K", "shared-global", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(AppOwn, Env, TenantAcme, "K", "own-acme", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(AppShared, Env, TenantAcme, "K", "shared-acme", false, t, null), CancellationToken.None);

        // Unrelated scope and unrelated environment — must be excluded.
        await _store.UpsertAsync(new ConfigEntry("OtherApp", Env, string.Empty, "K", "other", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(AppOwn, "OtherEnv", string.Empty, "K", "envk", false, t, null), CancellationToken.None);

        var results = await _store.GetAllScopedForAllTenantsAsync([AppShared, AppOwn], Env, CancellationToken.None);

        results.Count.ShouldBe(4);
        results.ShouldContain(e => e.AppName == AppOwn && e.TenantId == string.Empty);
        results.ShouldContain(e => e.AppName == AppShared && e.TenantId == string.Empty);
        results.ShouldContain(e => e.AppName == AppOwn && e.TenantId == TenantAcme);
        results.ShouldContain(e => e.AppName == AppShared && e.TenantId == TenantAcme);
    }

    [TimedFact(30_000)]
    public async Task GetAllScopedForAllTenants_OrdersByAppNamePositionInInputList()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(AppOwn, Env, string.Empty, "K", "own", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(AppShared, Env, string.Empty, "K", "shared", false, t, null), CancellationToken.None);

        var sharedFirst = await _store.GetAllScopedForAllTenantsAsync([AppShared, AppOwn], Env, CancellationToken.None);
        sharedFirst.Count.ShouldBe(2);
        sharedFirst[0].AppName.ShouldBe(AppShared);
        sharedFirst[1].AppName.ShouldBe(AppOwn);

        var ownFirst = await _store.GetAllScopedForAllTenantsAsync([AppOwn, AppShared], Env, CancellationToken.None);
        ownFirst.Count.ShouldBe(2);
        ownFirst[0].AppName.ShouldBe(AppOwn);
        ownFirst[1].AppName.ShouldBe(AppShared);
    }

    [TimedFact(30_000)]
    public async Task GetLatestModifiedUtcScopedAcrossAllTenants_ReturnsMaxAcrossAllRows()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        await _store.UpsertAsync(new ConfigEntry(AppOwn, Env, string.Empty, "A", "a", false, t1, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(AppShared, Env, TenantAcme, "B", "b", false, t2, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(AppOwn, Env, TenantAcme, "C", "c", false, t3, null), CancellationToken.None);

        var watermark = await _store.GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
            [AppShared, AppOwn], Env, CancellationToken.None);

        watermark.ShouldNotBeNull();
        watermark!.Value.ShouldBe(t2);
    }
}
