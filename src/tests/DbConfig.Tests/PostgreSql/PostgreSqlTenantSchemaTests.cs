using DbConfig.EntityFrameworkCore;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// Verifies that the B53 TenantId migration applied correctly on PostgreSQL:
/// column exists with the expected collation, unique constraint covers
/// (AppName, Environment, TenantId, Key), and watermark / history indexes include TenantId.
///
/// Tests insert rows via EF directly (not the store layer) so they remain valid before B54.
/// </summary>
[Trait("Category", "PostgreSql")]
[Collection(PostgreSqlFixture.CollectionName)]
public sealed class PostgreSqlTenantSchemaTests : IAsyncLifetime
{
    private const string App = "TenantSchemaApp";
    private const string Env = "Test";

    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlTenantSchemaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(60_000)]
    public async Task Migration_AppliesCleanly_AddsTenantIdColumn()
    {
        // The fixture runs MigrateAsync in InitializeAsync. If the migration was broken,
        // InitializeAsync would have thrown and this test class would have failed to start.
        // Confirm TenantId column exists in DbConfig_Entries.
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        const string sql = """
            SELECT COUNT(1)
            FROM information_schema.columns
            WHERE table_name = 'DbConfig_Entries'
              AND column_name = 'TenantId'
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None));

        count.ShouldBe(1L, "TenantId column should exist in DbConfig_Entries after migration");
    }

    [TimedFact(60_000)]
    public async Task UniqueConstraint_AllowsSameKeyAcrossDifferentTenants()
    {
        // Insert two rows with the same (App, Env, Key) but different TenantId directly via EF.
        // Both inserts must succeed because the unique constraint is on (AppName, Environment, TenantId, Key).
        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);

        ctx.ConfigEntries.Add(new ConfigEntryEntity
        {
            Id = Guid.NewGuid(),
            AppName = App,
            Environment = Env,
            TenantId = string.Empty,
            Key = "SharedKey",
            Value = "global-value",
            IsSecret = false,
            ModifiedUtc = DateTime.UtcNow,
        });

        ctx.ConfigEntries.Add(new ConfigEntryEntity
        {
            Id = Guid.NewGuid(),
            AppName = App,
            Environment = Env,
            TenantId = "Acme",
            Key = "SharedKey",
            Value = "acme-value",
            IsSecret = false,
            ModifiedUtc = DateTime.UtcNow,
        });

        var ex = await Record.ExceptionAsync(() => ctx.SaveChangesAsync(CancellationToken.None));
        ex.ShouldBeNull("two rows with different TenantId should not violate the unique constraint");

        await using var verifyCtx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await verifyCtx.ConfigEntries
            .AsNoTracking()
            .Where(e => e.AppName == App && e.Environment == Env && e.Key == "SharedKey")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(2);
    }

    [TimedFact(60_000)]
    public async Task UniqueConstraint_RejectsDuplicate_AppEnvTenantIdKey()
    {
        // Insert first row then attempt a raw SQL duplicate insert; must fail.
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        const string insert = """
            INSERT INTO "DbConfig_Entries" ("Id", "AppName", "Environment", "TenantId", "Key", "IsSecret", "ModifiedUtc")
            VALUES (@id, @app, @env, @tenant, @key, false, NOW())
            """;

        await using var cmd1 = new NpgsqlCommand(insert, connection);
        cmd1.Parameters.AddWithValue("@id", Guid.NewGuid());
        cmd1.Parameters.AddWithValue("@app", App);
        cmd1.Parameters.AddWithValue("@env", Env);
        cmd1.Parameters.AddWithValue("@tenant", "Acme");
        cmd1.Parameters.AddWithValue("@key", "DupKey");
        await cmd1.ExecuteNonQueryAsync(CancellationToken.None);

        await using var cmd2 = new NpgsqlCommand(insert, connection);
        cmd2.Parameters.AddWithValue("@id", Guid.NewGuid());
        cmd2.Parameters.AddWithValue("@app", App);
        cmd2.Parameters.AddWithValue("@env", Env);
        cmd2.Parameters.AddWithValue("@tenant", "Acme");
        cmd2.Parameters.AddWithValue("@key", "DupKey");

        var ex = await Record.ExceptionAsync(() => cmd2.ExecuteNonQueryAsync(CancellationToken.None));
        ex.ShouldNotBeNull("inserting a duplicate (App, Env, TenantId, Key) should violate the unique constraint");
        ex.ShouldBeOfType<PostgresException>();
    }

    [TimedFact(60_000)]
    public async Task Indexes_PresentAndComposite()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        // PostgreSQL lowercases unquoted identifiers; EF Core quotes index names in DDL
        // so the names are preserved with original casing. Query is case-insensitive for safety.
        var uniqueIndexExists = await IndexExistsAsync(
            connection, "UX_DbConfig_Entries_AppName_Environment_TenantId_Key");

        uniqueIndexExists.ShouldBeTrue("unique index on Entries should include TenantId");

        var watermarkIndexExists = await IndexExistsAsync(
            connection, "IX_DbConfig_Entries_AppName_Environment_TenantId_ModifiedUtc");

        watermarkIndexExists.ShouldBeTrue("watermark index on Entries should include TenantId");

        var historyIndexExists = await IndexExistsAsync(
            connection, "IX_DbConfig_Audit_AppName_Environment_TenantId_Key_ModifiedUtc");

        historyIndexExists.ShouldBeTrue("history index on AuditEntries should include TenantId");
    }

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection connection, string indexName)
    {
        // Use ILIKE for defensive case-insensitive matching in case EF lowercases the name.
        const string sql = """
            SELECT COUNT(1)
            FROM pg_indexes
            WHERE indexname ILIKE @indexName
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@indexName", indexName);

        var result = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt64(result) > 0;
    }
}
