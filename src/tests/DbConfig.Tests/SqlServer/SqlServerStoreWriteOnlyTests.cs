using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Verifies that <see cref="EfCoreConfigStore"/> persists <see cref="ConfigEntryRecord.IsWriteOnly"/>,
/// forces encryption for write-only entries, flags their audit rows, and that the migrator adds
/// the column to databases created before the flag existed.
/// </summary>
[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerStoreWriteOnlyTests : IAsyncLifetime
{
    private const string App = "WriteOnlyApp";
    private const string Env = "Production";
    private const string UpgradeSchema = "write_only_upgrade";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigStore _store = null!;
    private EfCoreConfigAuditStore _auditStore = null!;

    public SqlServerStoreWriteOnlyTests(SqlServerFixture fixture)
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
    public async Task Upsert_WriteOnlyWithoutIsSecret_PersistsFlagAndForcesIsSecret()
    {
        await _store.UpsertAsync(WriteOnlyEntry("Api:Key", "plain-api-key"), CancellationToken.None);

        var fetched = await _store.GetAsync(App, Env, "Api:Key", CancellationToken.None);

        fetched.ShouldNotBeNull();
        fetched!.IsWriteOnly.ShouldBeTrue();
        fetched.IsSecret.ShouldBeTrue();
        fetched.Value.ShouldBe("plain-api-key");
    }

    [TimedFact(30_000)]
    public async Task Upsert_WriteOnly_ValueStoredAsCiphertext()
    {
        await _store.UpsertAsync(WriteOnlyEntry("Db:Password", "hunter2"), CancellationToken.None);

        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var raw = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == App)
            .Where(x => x.Key == "Db:Password")
            .FirstOrDefaultAsync(CancellationToken.None);

        raw.ShouldNotBeNull();
        raw!.IsWriteOnly.ShouldBeTrue();
        raw.IsSecret.ShouldBeTrue();
        raw.Value.ShouldNotBeNull();
        raw.Value.ShouldNotBe("hunter2");
    }

    [TimedFact(30_000)]
    public async Task QueryAsync_WriteOnlyEntry_ReturnsFlag()
    {
        await _store.UpsertAsync(WriteOnlyEntry("Query:Key", "v"), CancellationToken.None);

        var entries = await _store.QueryAsync(App, Env, tenantId: null, keyPrefix: "Query", take: 10, CancellationToken.None);

        entries.ShouldHaveSingleItem().IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task Upsert_WriteOnlyInsert_AuditRowIsWriteOnly()
    {
        await _store.UpsertAsync(WriteOnlyEntry("Audit:Insert", "v1"), CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "Audit:Insert", 10, CancellationToken.None);

        var row = history.ShouldHaveSingleItem();
        row.Action.ShouldBe(ConfigAuditAction.Insert);
        row.IsWriteOnly.ShouldBeTrue();
        row.IsSecret.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task Upsert_DowngradeFromWriteOnly_AuditRowIsWriteOnly()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(WriteOnlyEntry("Audit:Downgrade", "secret-v1", t), CancellationToken.None);
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Audit:Downgrade", "plain-v2", false, t.AddSeconds(1), "tester"),
            CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "Audit:Downgrade", 10, CancellationToken.None);

        history.Count.ShouldBe(2);
        history[0].Action.ShouldBe(ConfigAuditAction.Update);
        history[0].IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task Upsert_PlainSecret_AuditRowIsNotWriteOnly()
    {
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Audit:Plain", "s", true, DateTimeOffset.UtcNow, "tester"),
            CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "Audit:Plain", 10, CancellationToken.None);

        history.ShouldHaveSingleItem().IsWriteOnly.ShouldBeFalse();
    }

    [TimedFact(30_000)]
    public async Task DeleteAsync_WriteOnlyEntry_AuditRowIsWriteOnly()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(WriteOnlyEntry("Audit:Delete", "v1", t), CancellationToken.None);

        await _store.DeleteAsync(App, Env, "Audit:Delete", CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "Audit:Delete", 10, CancellationToken.None);

        history.Count.ShouldBe(2);
        history.Single(x => x.Action == ConfigAuditAction.Delete).IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task Upsert_UpgradeToWriteOnly_PersistsFlagAndAuditRowIsWriteOnly()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Upgrade:Key", "plain-v1", true, t, "tester"),
            CancellationToken.None);

        await _store.UpsertAsync(WriteOnlyEntry("Upgrade:Key", "rotated-v2", t.AddSeconds(1)), CancellationToken.None);

        var stored = await _store.GetAsync(App, Env, "Upgrade:Key", CancellationToken.None);
        stored.ShouldNotBeNull();
        stored!.IsWriteOnly.ShouldBeTrue();
        var history = await _auditStore.GetHistoryAsync(App, Env, "Upgrade:Key", 10, CancellationToken.None);
        history[0].Action.ShouldBe(ConfigAuditAction.Update);
        history[0].IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task Upsert_DowngradeFromWriteOnly_ClearsStoredFlag()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(WriteOnlyEntry("Downgrade:Key", "secret-v1", t), CancellationToken.None);

        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Downgrade:Key", "plain-v2", false, t.AddSeconds(1), "tester"),
            CancellationToken.None);

        var stored = await _store.GetAsync(App, Env, "Downgrade:Key", CancellationToken.None);
        stored.ShouldNotBeNull();
        stored!.IsWriteOnly.ShouldBeFalse();
        stored.IsSecret.ShouldBeFalse();
        stored.Value.ShouldBe("plain-v2");
    }

    [TimedFact(30_000)]
    public async Task GetForTenantAsync_WriteOnlyTenantEntry_ReturnsFlag()
    {
        await _store.UpsertAsync(WriteOnlyEntry("Tenant:Key", "acme-secret") with { TenantId = "Acme" }, CancellationToken.None);

        var fetched = await _store.GetForTenantAsync(App, Env, "Acme", "Tenant:Key", CancellationToken.None);

        fetched.ShouldNotBeNull();
        fetched!.IsWriteOnly.ShouldBeTrue();
        fetched.Value.ShouldBe("acme-secret");
    }

    [TimedFact(30_000)]
    public async Task DeleteForTenantAsync_WriteOnlyTenantEntry_AuditRowIsWriteOnly()
    {
        await _store.UpsertAsync(WriteOnlyEntry("TenantDelete:Key", "v1") with { TenantId = "Acme" }, CancellationToken.None);

        await _store.DeleteForTenantAsync(App, Env, "Acme", "TenantDelete:Key", CancellationToken.None);

        var history = await _auditStore.GetHistoryForTenantAsync(App, Env, "Acme", "TenantDelete:Key", 10, CancellationToken.None);
        history.Count.ShouldBe(2);
        history.ShouldAllBe(x => x.IsWriteOnly);
    }

    [TimedFact(30_000)]
    public async Task AuditQueryAsync_WriteOnlyEntry_ReturnsFlag()
    {
        await _store.UpsertAsync(WriteOnlyEntry("AuditQuery:Key", "v1"), CancellationToken.None);

        var rows = await _auditStore.QueryAsync(App, Env, tenantId: null, keyPrefix: "AuditQuery", action: null, take: 10, CancellationToken.None);

        rows.ShouldHaveSingleItem().IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task GetHistoryAsync_PlainEntryUpgradedToWriteOnly_FlagsPreUpgradeRowsAndDecrypts()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Plain:Upgrade", "plain-text", false, t, "tester"),
            CancellationToken.None);
        await _store.UpsertAsync(WriteOnlyEntry("Plain:Upgrade", "now-secret", t.AddSeconds(1)), CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "Plain:Upgrade", 10, CancellationToken.None);

        history.Count.ShouldBe(2);
        history.ShouldAllBe(x => x.IsWriteOnly);
        history[0].OldValue.ShouldBe("plain-text");
        history[0].NewValue.ShouldBe("now-secret");
    }

    [TimedFact(30_000)]
    public async Task AuditQueryAsync_KeyUpgradedToWriteOnly_FlagsPreUpgradeRows()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "AuditUpgrade:Key", "same-value", true, t, "tester"),
            CancellationToken.None);
        await _store.UpsertAsync(WriteOnlyEntry("AuditUpgrade:Key", "same-value", t.AddSeconds(1)), CancellationToken.None);

        var rows = await _auditStore.QueryAsync(App, Env, tenantId: null, keyPrefix: "AuditUpgrade", action: null, take: 10, CancellationToken.None);

        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(x => x.IsWriteOnly);
    }

    [TimedFact(30_000)]
    public async Task GetAllAsync_WriteOnlyEntry_ReturnsFlag()
    {
        await _store.UpsertAsync(WriteOnlyEntry("GetAll:Key", "v"), CancellationToken.None);

        var entries = await _store.GetAllAsync(App, Env, CancellationToken.None);

        entries.ShouldHaveSingleItem().IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact(30_000)]
    public async Task GetAllScopedForAllTenantsAsync_WriteOnlyTenantEntry_ReturnsFlag()
    {
        await _store.UpsertAsync(WriteOnlyEntry("Polling:Key", "v") with { TenantId = "Acme" }, CancellationToken.None);

        var entries = await _store.GetAllScopedForAllTenantsAsync([App], Env, CancellationToken.None);

        entries.ShouldHaveSingleItem().IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact(60_000)]
    public async Task Migrate_ExistingSchemaWithoutColumn_AddsColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        await SqlServerDbConfigMigrator.MigrateAsync(_fixture.ConnectionString, schema: UpgradeSchema, ct);
        await ExecuteAsync(
            $"""
            DELETE FROM [{UpgradeSchema}].[ConfigEntries];
            INSERT INTO [{UpgradeSchema}].[ConfigEntries] ([Id], [Scope], [Environment], [TenantId], [Key], [Value], [IsSecret], [ModifiedUtc], [ModifiedBy])
            VALUES (NEWID(), N'Legacy', N'Production', N'', N'Legacy:Key', N'legacy-value', 0, SYSDATETIMEOFFSET(), NULL);
            DELETE FROM [{UpgradeSchema}].[AuditEntries];
            INSERT INTO [{UpgradeSchema}].[AuditEntries] ([Id], [Scope], [Environment], [TenantId], [Key], [OldValue], [NewValue], [IsSecret], [Action], [ModifiedUtc], [ModifiedBy])
            VALUES (NEWID(), N'Legacy', N'Production', N'', N'Legacy:Key', NULL, N'legacy-value', 0, N'Insert', SYSDATETIMEOFFSET(), NULL);
            ALTER TABLE [{UpgradeSchema}].[ConfigEntries] DROP CONSTRAINT [DF_ConfigEntries_IsWriteOnly];
            ALTER TABLE [{UpgradeSchema}].[ConfigEntries] DROP COLUMN [IsWriteOnly];
            ALTER TABLE [{UpgradeSchema}].[AuditEntries] DROP CONSTRAINT [DF_AuditEntries_IsWriteOnly];
            ALTER TABLE [{UpgradeSchema}].[AuditEntries] DROP COLUMN [IsWriteOnly];
            """,
            ct);

        await SqlServerDbConfigMigrator.MigrateAsync(_fixture.ConnectionString, schema: UpgradeSchema, ct);
        await SqlServerDbConfigMigrator.MigrateAsync(_fixture.ConnectionString, schema: UpgradeSchema, ct);

        (await ColumnExistsAsync("ConfigEntries", ct)).ShouldBeTrue();
        (await ColumnExistsAsync("AuditEntries", ct)).ShouldBeTrue();
        (await ScalarAsync($"SELECT COUNT(*) FROM [{UpgradeSchema}].[ConfigEntries] WHERE [Key] = N'Legacy:Key' AND [IsWriteOnly] = 0", ct)).ShouldBe(1);
        (await ScalarAsync($"SELECT COUNT(*) FROM [{UpgradeSchema}].[AuditEntries] WHERE [Key] = N'Legacy:Key' AND [IsWriteOnly] = 0 AND [NewValue] = N'legacy-value'", ct)).ShouldBe(1);
        (await ScalarAsync($"SELECT COUNT(*) FROM [{UpgradeSchema}].[ConfigEntries] WHERE [Key] = N'Legacy:Key' AND [Value] = N'legacy-value'", ct)).ShouldBe(1);
    }

    private static ConfigEntryRecord WriteOnlyEntry(string key, string value, DateTimeOffset? modifiedUtc = null)
    {
        return new ConfigEntryRecord(App, Env, string.Empty, key, value, false, modifiedUtc ?? DateTimeOffset.UtcNow, "tester", IsWriteOnly: true);
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<bool> ColumnExistsAsync(string table, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COL_LENGTH(N'[{UpgradeSchema}].[{table}]', N'IsWriteOnly')";
        var result = await cmd.ExecuteScalarAsync(ct);

        return result is not null and not DBNull;
    }

    private async Task<long> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);

        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
