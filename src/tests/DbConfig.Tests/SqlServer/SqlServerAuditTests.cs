using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Integration tests for the audit log on SQL Server.
/// Exercises EfCoreConfigStore audit writes and EfCoreConfigAuditStore reads.
/// </summary>
[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerAuditTests : IAsyncLifetime
{
    private const string App = "AuditApp";
    private const string Env = "AuditEnv";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigStore _store = null!;
    private EfCoreConfigAuditStore _auditStore = null!;

    public SqlServerAuditTests(SqlServerFixture fixture)
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
            _fixture.Encryptor,
            enableAuditLog: true);

        _auditStore = new EfCoreConfigAuditStore(_fixture.DbContextFactory, _fixture.Encryptor);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(30_000)]
    public async Task Upsert_Insert_WritesAuditRowWithActionInsert()
    {
        var entry = new ConfigEntry(App, Env, string.Empty, "Key1", "value1", false, DateTimeOffset.UtcNow, "tester");

        await _store.UpsertAsync(entry, CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "Key1", 10, CancellationToken.None);

        history.ShouldHaveSingleItem();
        history[0].Action.ShouldBe(ConfigAuditAction.Insert);
        history[0].OldValue.ShouldBeNull();
        history[0].NewValue.ShouldBe("value1");
        history[0].ModifiedBy.ShouldBe("tester");
    }

    [TimedFact(30_000)]
    public async Task Upsert_Update_WritesAuditRowWithOldAndNewValues()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "UpdateKey", "old-value", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "UpdateKey", "new-value", false, t.AddSeconds(1), "updater"), CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "UpdateKey", 10, CancellationToken.None);

        history.Count.ShouldBe(2);

        // Most recent first
        history[0].Action.ShouldBe(ConfigAuditAction.Update);
        history[0].OldValue.ShouldBe("old-value");
        history[0].NewValue.ShouldBe("new-value");
        history[0].ModifiedBy.ShouldBe("updater");

        history[1].Action.ShouldBe(ConfigAuditAction.Insert);
    }

    [TimedFact(30_000)]
    public async Task Delete_WritesAuditRowWithOldValueAndNullNewValue()
    {
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "DeleteKey", "to-delete", false, DateTimeOffset.UtcNow, null), CancellationToken.None);

        await _store.DeleteAsync(App, Env, "DeleteKey", CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "DeleteKey", 10, CancellationToken.None);

        history.Count.ShouldBe(2);
        history[0].Action.ShouldBe(ConfigAuditAction.Delete);
        history[0].OldValue.ShouldBe("to-delete");
        history[0].NewValue.ShouldBeNull();
    }

    [TimedFact(30_000)]
    public async Task SecretEntry_AuditRowStoresCiphertext()
    {
        const string plaintext = "super-secret-audit";
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "SecretAuditKey", plaintext, true, DateTimeOffset.UtcNow, null), CancellationToken.None);

        // Query audit table directly — should be ciphertext, NOT plaintext.
        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var raw = await context.AuditEntries
            .AsNoTracking()
            .Where(x => x.AppName == App && x.Environment == Env && x.Key == "SecretAuditKey")
            .FirstOrDefaultAsync(CancellationToken.None);

        raw.ShouldNotBeNull();
        raw!.NewValue.ShouldNotBeNull();
        raw.NewValue.ShouldNotBe(plaintext);
        raw.IsSecret.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task Audit_GetHistoryAsync_OrderedByModifiedUtcDesc_ReturnsTakeLimit()
    {
        const string key = "OrderedKey";
        var t = DateTimeOffset.UtcNow;

        // Insert + 3 updates
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, key, "v0", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, key, "v1", false, t.AddSeconds(1), null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, key, "v2", false, t.AddSeconds(2), null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, key, "v3", false, t.AddSeconds(3), null), CancellationToken.None);

        // take=2 should return the 2 most recent
        var history = await _auditStore.GetHistoryAsync(App, Env, key, 2, CancellationToken.None);

        history.Count.ShouldBe(2);
        history[0].NewValue.ShouldBe("v3");
        history[1].NewValue.ShouldBe("v2");
    }

    [TimedFact(30_000)]
    public async Task Audit_GetHistoryAsync_DecryptsSecretValues()
    {
        const string plaintext = "decrypted-secret";
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "DecryptKey", plaintext, true, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "DecryptKey", 10, CancellationToken.None);

        history.ShouldHaveSingleItem();
        history[0].NewValue.ShouldBe(plaintext);
        history[0].IsSecret.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task Audit_GetHistoryAsync_KeyNotFound_ReturnsEmpty()
    {
        var history = await _auditStore.GetHistoryAsync(App, Env, "NonExistentKey", 10, CancellationToken.None);

        history.ShouldBeEmpty();
    }

    [TimedFact(30_000)]
    public async Task EnableAuditLog_False_NoAuditRowsWritten()
    {
        var storeNoAudit = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new SqlServerUniqueConstraintDetector(),
            TimeProvider.System,
            _fixture.Encryptor,
            enableAuditLog: false);

        await storeNoAudit.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "NoAuditKey", "value", false, DateTimeOffset.UtcNow, null), CancellationToken.None);
        await storeNoAudit.DeleteAsync(App, Env, "NoAuditKey", CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "NoAuditKey", 10, CancellationToken.None);

        history.ShouldBeEmpty();
    }

    [TimedFact(30_000)]
    public async Task Atomicity_AuditWriteAndMutation_InSameTransaction()
    {
        // Verify that the audit row is committed alongside the mutation.
        // Both should be visible after a successful SaveChangesAsync.
        var entry = new ConfigEntry(App, Env, string.Empty, "AtomicKey", "atomic-value", false, DateTimeOffset.UtcNow, "atomic-user");

        await _store.UpsertAsync(entry, CancellationToken.None);

        // Confirm the config entry exists.
        var stored = await _store.GetAsync(App, Env, "AtomicKey", CancellationToken.None);
        stored.ShouldNotBeNull();
        stored!.Value.ShouldBe("atomic-value");

        // Confirm the audit row exists in the same transaction.
        var history = await _auditStore.GetHistoryAsync(App, Env, "AtomicKey", 10, CancellationToken.None);
        history.ShouldHaveSingleItem();
        history[0].Action.ShouldBe(ConfigAuditAction.Insert);
        history[0].ModifiedBy.ShouldBe("atomic-user");
    }

    [TimedFact(30_000)]
    public async Task IsSecret_FlipFromTrueToFalse_HistoryReturnsBothValuesCorrectly()
    {
        // Uses PassthroughConfigEncryptor so every row in GetHistoryAsync is readable regardless
        // of the IsSecret flag on the audit row. This lets us assert value correctness across
        // all 3 rows without being blocked by DataProtection key semantics on flipped rows.
        var passthroughEncryptor = new PassthroughConfigEncryptor();
        var store = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new SqlServerUniqueConstraintDetector(),
            TimeProvider.System,
            passthroughEncryptor,
            enableAuditLog: true);
        var auditStore = new EfCoreConfigAuditStore(_fixture.DbContextFactory, passthroughEncryptor);

        const string App2 = "FlipTTFApp";
        var t = DateTimeOffset.UtcNow;

        // Step 1: Insert with IsSecret=true → audit row 1 (Insert, OldValue=null, NewValue="secret-v1").
        await store.UpsertAsync(new ConfigEntry(App2, Env, string.Empty, "FlipKey", "secret-v1", true, t, null), CancellationToken.None);

        // Step 2: Update with IsSecret=true → audit row 2 (Update, OldValue="secret-v1", NewValue="secret-v2").
        await store.UpsertAsync(new ConfigEntry(App2, Env, string.Empty, "FlipKey", "secret-v2", true, t.AddSeconds(1), null), CancellationToken.None);

        // Step 3: Flip to IsSecret=false → audit row 3 (Update, IsSecret=false in snapshot,
        // OldValue="secret-v2" (stored as plaintext by passthrough), NewValue="plain-v3").
        await store.UpsertAsync(new ConfigEntry(App2, Env, string.Empty, "FlipKey", "plain-v3", false, t.AddSeconds(2), null), CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App2, Env, "FlipKey", 10, CancellationToken.None);

        history.Count.ShouldBe(3);

        // Row 0 — most recent: IsSecret=false audit snapshot. GetHistoryAsync will NOT decrypt
        // OldValue or NewValue (because IsSecret=false on the row). Passthrough means both are verbatim.
        history[0].Action.ShouldBe(ConfigAuditAction.Update);
        history[0].IsSecret.ShouldBeFalse();
        history[0].OldValue.ShouldBe("secret-v2");
        history[0].NewValue.ShouldBe("plain-v3");

        // Row 1 — IsSecret=true on both sides; passthrough encryptor returns values verbatim.
        history[1].Action.ShouldBe(ConfigAuditAction.Update);
        history[1].IsSecret.ShouldBeTrue();
        history[1].OldValue.ShouldBe("secret-v1");
        history[1].NewValue.ShouldBe("secret-v2");

        // Row 2 — initial insert.
        history[2].Action.ShouldBe(ConfigAuditAction.Insert);
        history[2].IsSecret.ShouldBeTrue();
        history[2].OldValue.ShouldBeNull();
        history[2].NewValue.ShouldBe("secret-v1");
    }

    [TimedFact(30_000)]
    public async Task IsSecret_FlipFromFalseToTrue_HistoryReturnsBothValuesCorrectly()
    {
        // Uses PassthroughConfigEncryptor so every row in GetHistoryAsync is readable regardless
        // of the IsSecret flag on the audit row. This lets us assert value correctness across
        // all 3 rows without being blocked by DataProtection key semantics on flipped rows.
        var passthroughEncryptor = new PassthroughConfigEncryptor();
        var store = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new SqlServerUniqueConstraintDetector(),
            TimeProvider.System,
            passthroughEncryptor,
            enableAuditLog: true);
        var auditStore = new EfCoreConfigAuditStore(_fixture.DbContextFactory, passthroughEncryptor);

        const string App2 = "FlipFTTApp";
        var t = DateTimeOffset.UtcNow;

        // Step 1: Insert with IsSecret=false → audit row 1 (Insert, OldValue=null, NewValue="plain-v1").
        await store.UpsertAsync(new ConfigEntry(App2, Env, string.Empty, "FlipKey", "plain-v1", false, t, null), CancellationToken.None);

        // Step 2: Update with IsSecret=false → audit row 2 (Update, OldValue="plain-v1", NewValue="plain-v2").
        await store.UpsertAsync(new ConfigEntry(App2, Env, string.Empty, "FlipKey", "plain-v2", false, t.AddSeconds(1), null), CancellationToken.None);

        // Step 3: Flip to IsSecret=true → audit row 3 (Update, IsSecret=true in snapshot,
        // OldValue="plain-v2" (raw stored plaintext), NewValue="secret-v3").
        await store.UpsertAsync(new ConfigEntry(App2, Env, string.Empty, "FlipKey", "secret-v3", true, t.AddSeconds(2), null), CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App2, Env, "FlipKey", 10, CancellationToken.None);

        history.Count.ShouldBe(3);

        // Row 0 — most recent: IsSecret=true audit snapshot. GetHistoryAsync calls Unprotect on both
        // OldValue and NewValue. Passthrough Unprotect is a no-op, so values are returned verbatim.
        history[0].Action.ShouldBe(ConfigAuditAction.Update);
        history[0].IsSecret.ShouldBeTrue();
        history[0].OldValue.ShouldBe("plain-v2");
        history[0].NewValue.ShouldBe("secret-v3");

        // Row 1 — IsSecret=false; plaintext values returned verbatim.
        history[1].Action.ShouldBe(ConfigAuditAction.Update);
        history[1].IsSecret.ShouldBeFalse();
        history[1].OldValue.ShouldBe("plain-v1");
        history[1].NewValue.ShouldBe("plain-v2");

        // Row 2 — initial insert, IsSecret=false.
        history[2].Action.ShouldBe(ConfigAuditAction.Insert);
        history[2].IsSecret.ShouldBeFalse();
        history[2].OldValue.ShouldBeNull();
        history[2].NewValue.ShouldBe("plain-v1");
    }
}
