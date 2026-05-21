using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Tests for <see cref="InMemoryConfigAuditStore"/> and the audit integration
/// in <see cref="InMemoryConfigStore"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryAuditStoreTests
{
    private const string App = "TestApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task GetHistoryAsync_ReturnsEmpty_WhenNoMutations()
    {
        var auditStore = new InMemoryConfigAuditStore();

        var history = await auditStore.GetHistoryAsync(App, Env, "SomeKey", 10, CancellationToken.None);

        history.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task Upsert_AddsAuditRow()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(null, auditStore, enableAuditLog: true);
        var entry = new ConfigEntryRecord(App, Env, string.Empty, "Key1", "value1", false, DateTimeOffset.UtcNow, "user1");

        await store.UpsertAsync(entry, CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "Key1", 10, CancellationToken.None);
        history.ShouldHaveSingleItem();
        history[0].Action.ShouldBe(ConfigAuditAction.Insert);
        history[0].NewValue.ShouldBe("value1");
        history[0].OldValue.ShouldBeNull();
        history[0].ModifiedBy.ShouldBe("user1");
    }

    [TimedFact]
    public async Task Upsert_Update_AddsAuditRowWithOldValue()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(null, auditStore, enableAuditLog: true);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "old", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "new", false, t.AddSeconds(1), "updater"), CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "Key1", 10, CancellationToken.None);
        history.Count.ShouldBe(2);

        // Most recent first
        history[0].Action.ShouldBe(ConfigAuditAction.Update);
        history[0].OldValue.ShouldBe("old");
        history[0].NewValue.ShouldBe("new");

        history[1].Action.ShouldBe(ConfigAuditAction.Insert);
    }

    [TimedFact]
    public async Task Delete_AddsAuditRow()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(null, auditStore, enableAuditLog: true);

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "ToDelete", "del-value", false, DateTimeOffset.UtcNow, null), CancellationToken.None);
        await store.DeleteAsync(App, Env, "ToDelete", CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "ToDelete", 10, CancellationToken.None);
        history.Count.ShouldBe(2);
        history[0].Action.ShouldBe(ConfigAuditAction.Delete);
        history[0].OldValue.ShouldBe("del-value");
        history[0].NewValue.ShouldBeNull();
    }

    [TimedFact]
    public async Task EnableAuditLog_False_NoRowsWritten()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(null, auditStore, enableAuditLog: false);

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "value1", false, DateTimeOffset.UtcNow, null), CancellationToken.None);
        await store.DeleteAsync(App, Env, "Key1", CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "Key1", 10, CancellationToken.None);
        history.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task GetHistoryAsync_OrderedByModifiedUtcDesc()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(null, auditStore, enableAuditLog: true);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "v0", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "v1", false, t.AddSeconds(1), null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "v2", false, t.AddSeconds(2), null), CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "Key1", 2, CancellationToken.None);

        history.Count.ShouldBe(2);
        history[0].NewValue.ShouldBe("v2");
        history[1].NewValue.ShouldBe("v1");
    }

    [TimedFact]
    public async Task GetHistoryAsync_KeyNotFound_ReturnsEmpty()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(null, auditStore, enableAuditLog: true);

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "OtherKey", "v", false, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "NonExistentKey", 10, CancellationToken.None);

        history.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task NoAuditStore_UpsertAndDelete_DoNotThrow()
    {
        // Store without audit sink — operations succeed silently
        var store = new InMemoryConfigStore();

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Key1", "v", false, DateTimeOffset.UtcNow, null), CancellationToken.None);
        await store.DeleteAsync(App, Env, "Key1", CancellationToken.None);

        var result = await store.GetAsync(App, Env, "Key1", CancellationToken.None);
        result.ShouldBeNull();
    }
}
