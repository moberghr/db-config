using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Verifies that <see cref="InMemoryConfigStore"/> treats <see cref="ConfigEntryRecord.IsWriteOnly"/>
/// the same way the EF Core store does: the flag persists, it forces <c>IsSecret</c>, and audit rows
/// carry it whenever the old or the new state is write-only.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryStoreWriteOnlyTests
{
    private const string App = "WriteOnlyApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task Upsert_WriteOnlyWithoutIsSecret_PersistsFlagAndForcesIsSecret()
    {
        var store = new InMemoryConfigStore();

        await store.UpsertAsync(WriteOnlyEntry("Api:Key", "plain-api-key"), CancellationToken.None);

        var fetched = await store.GetAsync(App, Env, "Api:Key", CancellationToken.None);
        fetched.ShouldNotBeNull();
        fetched!.IsWriteOnly.ShouldBeTrue();
        fetched.IsSecret.ShouldBeTrue();
        fetched.Value.ShouldBe("plain-api-key");
    }

    [TimedFact]
    public async Task Upsert_WriteOnlyInsert_AuditRowIsWriteOnly()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(encryptor: null, auditStore, enableAuditLog: true);

        await store.UpsertAsync(WriteOnlyEntry("Audit:Insert", "v1"), CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "Audit:Insert", 10, CancellationToken.None);
        var row = history.ShouldHaveSingleItem();
        row.IsWriteOnly.ShouldBeTrue();
        row.IsSecret.ShouldBeTrue();
    }

    [TimedFact]
    public async Task Upsert_DowngradeFromWriteOnly_AuditRowIsWriteOnly()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(encryptor: null, auditStore, enableAuditLog: true);
        var t = DateTimeOffset.UtcNow;
        await store.UpsertAsync(WriteOnlyEntry("Audit:Downgrade", "secret-v1", t), CancellationToken.None);

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Audit:Downgrade", "plain-v2", false, t.AddSeconds(1), "tester"),
            CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "Audit:Downgrade", 10, CancellationToken.None);
        history.Count.ShouldBe(2);
        history[0].Action.ShouldBe(ConfigAuditAction.Update);
        history[0].IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact]
    public async Task Upsert_PlainSecret_AuditRowIsNotWriteOnly()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(encryptor: null, auditStore, enableAuditLog: true);

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Audit:Plain", "s", true, DateTimeOffset.UtcNow, "tester"),
            CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "Audit:Plain", 10, CancellationToken.None);
        history.ShouldHaveSingleItem().IsWriteOnly.ShouldBeFalse();
    }

    [TimedFact]
    public async Task DeleteAsync_WriteOnlyEntry_AuditRowIsWriteOnly()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(encryptor: null, auditStore, enableAuditLog: true);
        await store.UpsertAsync(WriteOnlyEntry("Audit:Delete", "v1"), CancellationToken.None);

        await store.DeleteAsync(App, Env, "Audit:Delete", CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "Audit:Delete", 10, CancellationToken.None);
        history.Single(x => x.Action == ConfigAuditAction.Delete).IsWriteOnly.ShouldBeTrue();
    }

    private static ConfigEntryRecord WriteOnlyEntry(string key, string value, DateTimeOffset? modifiedUtc = null)
    {
        return new ConfigEntryRecord(App, Env, string.Empty, key, value, false, modifiedUtc ?? DateTimeOffset.UtcNow, "tester", IsWriteOnly: true);
    }
}
