using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Tests for tenant-aware store methods on <see cref="InMemoryConfigStore"/> and
/// <see cref="InMemoryConfigAuditStore"/> (B54).
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryStoreTenantTests
{
    private const string App = "TenantApp";
    private const string Env = "Test";
    private const string TenantAcme = "Acme";
    private const string TenantGlobex = "Globex";

    [TimedFact]
    public async Task Upsert_WithTenantId_StoresUnderTenant()
    {
        var store = new InMemoryConfigStore();
        var entry = new ConfigEntryRecord(App, Env, TenantAcme, "Key1", "acme-value", false, DateTimeOffset.UtcNow, null);

        await store.UpsertAsync(entry, CancellationToken.None);

        var result = await store.GetForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);
        result.ShouldNotBeNull();
        result!.Value.ShouldBe("acme-value");
        result.TenantId.ShouldBe(TenantAcme);
    }

    [TimedFact]
    public async Task GetForTenantAsync_TenantSpecific_ReturnsTenantValue()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "global-value", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "Key1", "acme-value", false, t, null), CancellationToken.None);

        var tenantResult = await store.GetForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);
        tenantResult.ShouldNotBeNull();
        tenantResult!.Value.ShouldBe("acme-value");
    }

    [TimedFact]
    public async Task GetForTenantAsync_TenantNotPresent_ReturnsNull()
    {
        // No fallback at store layer — fallback is in ITenantConfigReader (B55).
        var store = new InMemoryConfigStore();
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "global-value", false, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var result = await store.GetForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);

        result.ShouldBeNull();
    }

    [TimedFact]
    public async Task GetAllForTenantAsync_ScopedByTenant_ReturnsOnlyThatTenant()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey", "global", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "AcmeKey", "acme", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantGlobex, "GlobexKey", "globex", false, t, null), CancellationToken.None);

        var acmeEntries = await store.GetAllForTenantAsync(App, Env, TenantAcme, CancellationToken.None);

        acmeEntries.ShouldHaveSingleItem();
        acmeEntries[0].Key.ShouldBe("AcmeKey");
        acmeEntries[0].TenantId.ShouldBe(TenantAcme);
    }

    [TimedFact]
    public async Task GetAllForAllTenantsAsync_ReturnsEveryTenantEntry()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey", "global", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "AcmeKey", "acme", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantGlobex, "GlobexKey", "globex", false, t, null), CancellationToken.None);

        // Entry for a different app — must NOT appear
        await store.UpsertAsync(new ConfigEntryRecord("OtherApp", Env, TenantAcme, "OtherKey", "other", false, t, null), CancellationToken.None);

        var all = await store.GetAllForAllTenantsAsync(App, Env, CancellationToken.None);

        all.Count.ShouldBe(3);
        all.Select(x => x.Key).ShouldContain("GlobalKey");
        all.Select(x => x.Key).ShouldContain("AcmeKey");
        all.Select(x => x.Key).ShouldContain("GlobexKey");
    }

    [TimedFact]
    public async Task DeleteForTenantAsync_OnlyAffectsSpecifiedTenant()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "global", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "Key1", "acme", false, t, null), CancellationToken.None);

        await store.DeleteForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);

        var tenantResult = await store.GetForTenantAsync(App, Env, TenantAcme, "Key1", CancellationToken.None);
        tenantResult.ShouldBeNull("tenant-specific entry should be deleted");

        var globalResult = await store.GetAsync(App, Env, "Key1", CancellationToken.None);
        globalResult.ShouldNotBeNull("global entry must be unaffected");
        globalResult!.Value.ShouldBe("global");
    }

    [TimedFact]
    public async Task LegacyGetAllAsync_OnlyReturnsGlobalEntries()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey", "global", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "AcmeKey", "acme", false, t, null), CancellationToken.None);

        var result = await store.GetAllAsync(App, Env, CancellationToken.None);

        result.ShouldHaveSingleItem();
        result[0].Key.ShouldBe("GlobalKey");
        result[0].TenantId.ShouldBe(string.Empty);
    }

    [TimedFact]
    public async Task GetLatestModifiedUtcForTenantAsync_ReturnsHighestWatermarkForTenant()
    {
        var store = new InMemoryConfigStore();
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "A", "v", false, t1, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "B", "v", false, t2, null), CancellationToken.None);

        // A different tenant — should not affect the watermark for Acme
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantGlobex, "C", "v", false, t3, null), CancellationToken.None);

        var watermark = await store.GetLatestModifiedUtcForTenantAsync(App, Env, TenantAcme, CancellationToken.None);

        watermark.ShouldNotBeNull();
        watermark!.Value.ShouldBe(t2);
    }

    [TimedFact]
    public async Task AuditStore_GetHistoryForTenantAsync_FiltersByTenant()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(null, auditStore, enableAuditLog: true);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "global-v1", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "Key1", "acme-v1", false, t, null), CancellationToken.None);

        var acmeHistory = await auditStore.GetHistoryForTenantAsync(App, Env, TenantAcme, "Key1", 10, CancellationToken.None);
        acmeHistory.ShouldHaveSingleItem();
        acmeHistory[0].TenantId.ShouldBe(TenantAcme);
        acmeHistory[0].NewValue.ShouldBe("acme-v1");

        var globalHistory = await auditStore.GetHistoryAsync(App, Env, "Key1", 10, CancellationToken.None);
        globalHistory.ShouldHaveSingleItem();
        globalHistory[0].TenantId.ShouldBe(string.Empty);
        globalHistory[0].NewValue.ShouldBe("global-v1");
    }
}
