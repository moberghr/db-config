using DbConfig.EntityFrameworkCore;
using DbConfig.Tests.TestData;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Verifies that the B53 TenantId migration applied correctly on SQL Server:
/// column exists with the expected type and collation, unique constraint covers
/// (Scope, Environment, TenantId, Key), and watermark / history indexes include TenantId.
///
/// Tests insert rows via EF directly (not the store layer) so they remain valid before B54.
/// </summary>
[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerTenantSchemaTests : IAsyncLifetime
{
    private const string App = "TenantSchemaApp";
    private const string Env = "Test";

    private readonly SqlServerFixture _fixture;

    public SqlServerTenantSchemaTests(SqlServerFixture fixture)
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
        // Here we additionally confirm the column exists with the expected collation.
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        const string sql = """
            SELECT c.collation_name
            FROM sys.columns c
            INNER JOIN sys.tables t ON c.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name = 'ConfigEntries'
              AND c.name = 'TenantId'
              AND s.name = 'configuration'
            """;

        await using var command = new SqlCommand(sql, connection);
        var collation = await command.ExecuteScalarAsync(CancellationToken.None) as string;

        collation.ShouldNotBeNullOrEmpty();
        collation.ShouldBe("Latin1_General_100_BIN2");
    }

    [TimedFact(60_000)]
    public async Task UniqueConstraint_AllowsSameKeyAcrossDifferentTenants()
    {
        // Insert two rows with the same (App, Env, Key) but different TenantId directly via EF.
        // Both inserts must succeed because the unique constraint is on (Scope, Environment, TenantId, Key).
        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);

        ctx.ConfigEntries.Add(new ConfigEntry
        {
            Id = Guid.NewGuid(),
            Scope = App,
            Environment = Env,
            TenantId = string.Empty,
            Key = "SharedKey",
            Value = "global-value",
            IsSecret = false,
            ModifiedUtc = DateTime.UtcNow,
        });

        ctx.ConfigEntries.Add(new ConfigEntry
        {
            Id = Guid.NewGuid(),
            Scope = App,
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
            .Where(e => e.Scope == App && e.Environment == Env && e.Key == "SharedKey")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(2);
    }

    [TimedFact(60_000)]
    public async Task UniqueConstraint_RejectsDuplicate_AppEnvTenantIdKey()
    {
        // Insert first row then attempt a raw SQL duplicate insert; must fail.
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        const string insert = """
            INSERT INTO configuration.ConfigEntries (Id, Scope, Environment, TenantId, [Key], IsSecret, ModifiedUtc)
            VALUES (@id, @app, @env, @tenant, @key, 0, GETUTCDATE())
            """;

        await using var cmd1 = new SqlCommand(insert, connection);
        cmd1.Parameters.AddWithValue("@id", Guid.NewGuid());
        cmd1.Parameters.AddWithValue("@app", App);
        cmd1.Parameters.AddWithValue("@env", Env);
        cmd1.Parameters.AddWithValue("@tenant", "Acme");
        cmd1.Parameters.AddWithValue("@key", "DupKey");
        await cmd1.ExecuteNonQueryAsync(CancellationToken.None);

        await using var cmd2 = new SqlCommand(insert, connection);
        cmd2.Parameters.AddWithValue("@id", Guid.NewGuid());
        cmd2.Parameters.AddWithValue("@app", App);
        cmd2.Parameters.AddWithValue("@env", Env);
        cmd2.Parameters.AddWithValue("@tenant", "Acme");
        cmd2.Parameters.AddWithValue("@key", "DupKey");

        var ex = await Record.ExceptionAsync(() => cmd2.ExecuteNonQueryAsync(CancellationToken.None));
        ex.ShouldNotBeNull("inserting a duplicate (App, Env, TenantId, Key) should violate the unique constraint");
        ex.ShouldBeOfType<SqlException>();
    }

    [TimedFact(60_000)]
    public async Task Indexes_PresentAndComposite()
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var uniqueIndexExists = await IndexExistsAsync(
            connection, "ConfigEntries", "IX_ConfigEntries_Scope_Environment_TenantId_Key");

        uniqueIndexExists.ShouldBeTrue("unique index on Entries should include TenantId");

        var watermarkIndexExists = await IndexExistsAsync(
            connection, "ConfigEntries", "IX_ConfigEntries_Scope_Environment_TenantId_ModifiedUtc");

        watermarkIndexExists.ShouldBeTrue("watermark index on Entries should include TenantId");

        var historyIndexExists = await IndexExistsAsync(
            connection, "AuditEntries", "IX_AuditEntries_Scope_Environment_TenantId_Key_ModifiedUtc");

        historyIndexExists.ShouldBeTrue("history index on AuditEntries should include TenantId");
    }

    private static async Task<bool> IndexExistsAsync(SqlConnection connection, string tableName, string indexName)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name = @tableName
              AND i.name = @indexName
              AND s.name = 'configuration'
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@indexName", indexName);

        var result = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt32(result) > 0;
    }
}
