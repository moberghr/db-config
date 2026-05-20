using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Integration tests for tenant-aware store methods on SQL Server (B54).
/// Verifies that <see cref="EfCoreConfigStore"/> and <see cref="EfCoreConfigAuditStore"/>
/// correctly scope reads/writes/deletes by TenantId.
/// </summary>
[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerStoreTenantTests : IAsyncLifetime
{
    private const string App = "TenantApp";
    private const string Env = "Test";
    private const string TenantAcme = "Acme";
    private const string TenantGlobex = "Globex";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigStore _store = null!;
    private EfCoreConfigAuditStore _auditStore = null!;

    public SqlServerStoreTenantTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
        _store = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new SqlServerUniqueConstraintDetector(),
            TimeProvider.System,
            enableAuditLog: true);

        _auditStore = new EfCoreConfigAuditStore(
            _fixture.DbContextFactory,
            new PassthroughConfigEncryptor());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(60_000)]
    public async Task Upsert_WithTenantId_StoresUnderTenant()
    {
        var entry = new ConfigEntry(App, Env, TenantAcme, "Key1", "acme-value", false, DateTimeOffset.UtcNow, null);

        await _store.UpsertAsync(entry, CancellationToken.None);

        var result = await _store.GetForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);
        result.ShouldNotBeNull();
        result!.Value.ShouldBe("acme-value");
        result.TenantId.ShouldBe(TenantAcme);
    }

    [TimedFact(60_000)]
    public async Task GetForTenantAsync_TenantSpecific_ReturnsTenantValue()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key1", "global-value", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Key1", "acme-value", false, t, null), CancellationToken.None);

        var result = await _store.GetForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe("acme-value");
    }

    [TimedFact(60_000)]
    public async Task GetForTenantAsync_TenantNotPresent_ReturnsNull()
    {
        // No fallback at store layer — fallback is in ITenantConfigReader (B55).
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key1", "global-value", false, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var result = await _store.GetForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);

        result.ShouldBeNull();
    }

    [TimedFact(60_000)]
    public async Task GetAllForTenantAsync_ScopedByTenant_ReturnsOnlyThatTenant()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "GlobalKey", "global", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "AcmeKey", "acme", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, TenantGlobex, "GlobexKey", "globex", false, t, null), CancellationToken.None);

        var acmeEntries = await _store.GetAllForTenantAsync(App, Env, TenantAcme, CancellationToken.None);

        acmeEntries.ShouldHaveSingleItem();
        acmeEntries[0].Key.ShouldBe("AcmeKey");
        acmeEntries[0].TenantId.ShouldBe(TenantAcme);
    }

    [TimedFact(60_000)]
    public async Task GetAllForAllTenantsAsync_LoadsAcrossTenants()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "GlobalKey", "global", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "AcmeKey", "acme", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, TenantGlobex, "GlobexKey", "globex", false, t, null), CancellationToken.None);

        var all = await _store.GetAllForAllTenantsAsync(App, Env, CancellationToken.None);

        all.Count.ShouldBe(3);
        all.Select(x => x.Key).ShouldContain("GlobalKey");
        all.Select(x => x.Key).ShouldContain("AcmeKey");
        all.Select(x => x.Key).ShouldContain("GlobexKey");
    }

    [TimedFact(60_000)]
    public async Task DeleteForTenantAsync_OnlyAffectsSpecifiedTenant()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key1", "global", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Key1", "acme", false, t, null), CancellationToken.None);

        await _store.DeleteForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);

        var tenantResult = await _store.GetForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);
        tenantResult.ShouldBeNull("tenant-specific entry should be deleted");

        var globalResult = await _store.GetAsync(App, Env, "Key1", CancellationToken.None);
        globalResult.ShouldNotBeNull("global entry must be unaffected");
        globalResult!.Value.ShouldBe("global");
    }

    [TimedFact(60_000)]
    public async Task LegacyGetAllAsync_OnlyReturnsGlobalEntries()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "GlobalKey", "global", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "AcmeKey", "acme", false, t, null), CancellationToken.None);

        var result = await _store.GetAllAsync(App, Env, CancellationToken.None);

        result.ShouldHaveSingleItem();
        result[0].Key.ShouldBe("GlobalKey");
        result[0].TenantId.ShouldBe(string.Empty);
    }

    [TimedFact(60_000)]
    public async Task Upsert_TenantSpecific_AuditRowCarriesTenantId()
    {
        var entry = new ConfigEntry(App, Env, TenantAcme, "AuditKey", "acme-value", false, DateTimeOffset.UtcNow, "tester");

        await _store.UpsertAsync(entry, CancellationToken.None);

        // Verify the audit row in the DB carries the correct TenantId.
        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var auditRow = await context.AuditEntries
            .AsNoTracking()
            .Where(x => x.Scope == App && x.Environment == Env && x.Key == "AuditKey")
            .FirstOrDefaultAsync(CancellationToken.None);

        auditRow.ShouldNotBeNull();
        auditRow!.TenantId.ShouldBe(TenantAcme);
        auditRow.Action.ShouldBe(ConfigAuditAction.Insert.ToString());

        // Also verify via the audit store GetHistoryForTenantAsync
        var history = await _auditStore.GetHistoryForTenantAsync(App, Env, TenantAcme, "AuditKey", 10, CancellationToken.None);
        history.ShouldHaveSingleItem();
        history[0].TenantId.ShouldBe(TenantAcme);
        history[0].Action.ShouldBe(ConfigAuditAction.Insert);
    }
}
