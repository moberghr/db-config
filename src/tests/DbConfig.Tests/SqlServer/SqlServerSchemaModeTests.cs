using DbConfig.Core;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Verifies <see cref="SchemaMode"/> behavior on SQL Server: CreateIfMissing
/// (the default) auto-applies the raw-SQL initial-create script during AddDbConfig;
/// None skips it entirely. Also exercises the <see cref="SqlServerDbConfigMigrator"/>
/// public surface (script content + idempotent execution).
/// </summary>
[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerSchemaModeTests : IAsyncLifetime
{
    private const string App = "SchemaModeTestApp";
    private const string Env = "Test";

    private readonly SqlServerFixture _fixture;

    public SqlServerSchemaModeTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await DropDbConfigTablesAsync(_fixture.ConnectionString, ct);
    }

    public async ValueTask DisposeAsync()
    {
        // Restore the schema after each test so other tests in the SqlServer collection
        // that expect DbConfig tables to exist (most of them) keep working.
        await SqlServerDbConfigMigrator.MigrateAsync(
            _fixture.ConnectionString, schema: "configuration", CancellationToken.None);
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_CreateIfMissingMode_AutoAppliesMigrations()
    {
        var ct = TestContext.Current.CancellationToken;

        // Pre-condition: tables do NOT exist after the drop in InitializeAsync.
        (await TableExistsAsync(_fixture.ConnectionString, "ConfigEntries", ct)).ShouldBeFalse();

        // AddDbConfig with default SchemaMode = CreateIfMissing.
        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.UseSqlServer(_fixture.ConnectionString);
        });

        // Tables MUST exist after AddDbConfig returns.
        (await TableExistsAsync(_fixture.ConnectionString, "ConfigEntries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "AuditEntries", ct)).ShouldBeTrue();

        // A basic Upsert must round-trip through the now-ready schema.
        using var host = builder.Build();
        var store = host.Services.GetRequiredService<IConfigStore>();
        var entry = new ConfigEntryRecord(App, Env, string.Empty, "Key", "value", false, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(entry, ct);

        var all = await store.GetAllAsync(App, Env, ct);
        all.ShouldHaveSingleItem();
        all[0].Value.ShouldBe("value");
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_NoneMode_DoesNotApplyMigrations()
    {
        var ct = TestContext.Current.CancellationToken;

        (await TableExistsAsync(_fixture.ConnectionString, "ConfigEntries", ct)).ShouldBeFalse();

        // AddDbConfig with SchemaMode.None must NOT migrate. The polling provider's
        // first Load() then fails because the table doesn't exist. We accept either
        // an exception at AddDbConfig time (from Load) or a clear failure later — what
        // we verify is that no tables were created.
        var builder = Host.CreateApplicationBuilder();
        var exception = Record.Exception(() => builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.Options.SchemaMode = SchemaMode.None;
            b.UseSqlServer(_fixture.ConnectionString);
        }));

        // The polling provider's Load() runs synchronously inside AddDbConfig and fails
        // because the table doesn't exist. The failure must surface as an InvalidOperationException
        // (wrapped by DbConfigConfigurationProvider — the raw provider exception is the inner one).
        exception.ShouldBeOfType<InvalidOperationException>();
        (await TableExistsAsync(_fixture.ConnectionString, "ConfigEntries", ct)).ShouldBeFalse();
    }

    [TimedFact]
    public void GetCreateScript_ContainsExpectedTablesAndIdempotencyGuards()
    {
        var sql = SqlServerDbConfigMigrator.GetCreateScript();

        sql.ShouldContain("ConfigEntries");
        sql.ShouldContain("AuditEntries");

        // Idempotency: every CREATE statement must be guarded so re-applying is safe.
        sql.ShouldContain("IF NOT EXISTS");
    }

    [TimedFact]
    public void GetCreateScript_SubstitutesSchemaPlaceholder()
    {
        var defaultSchema = SqlServerDbConfigMigrator.GetCreateScript();
        var customSchema = SqlServerDbConfigMigrator.GetCreateScript("my_schema");

        defaultSchema.ShouldContain("configuration");
        defaultSchema.ShouldNotContain("{schema}");

        customSchema.ShouldContain("my_schema");
        customSchema.ShouldNotContain("{schema}");
        customSchema.ShouldNotContain("[configuration]");
    }

    [TimedFact(30_000)]
    public async Task MigrateAsync_AppliesScript_AndIsIdempotent_DataSurvivesReApply()
    {
        var ct = TestContext.Current.CancellationToken;
        (await TableExistsAsync(_fixture.ConnectionString, "ConfigEntries", ct)).ShouldBeFalse();

        await SqlServerDbConfigMigrator.MigrateAsync(_fixture.ConnectionString, schema: "configuration", ct);

        (await TableExistsAsync(_fixture.ConnectionString, "ConfigEntries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "AuditEntries", ct)).ShouldBeTrue();

        // Insert a sentinel row so we can verify the re-apply does NOT recreate (DROP+CREATE) tables.
        // A buggy idempotency implementation that wraps CREATE TABLE in DROP+CREATE would silently
        // destroy this row even though "the tables still exist" afterwards.
        var sentinelId = Guid.NewGuid();
        await InsertSentinelAsync(_fixture.ConnectionString, sentinelId, ct);

        // Re-apply: must not throw, tables must still exist, sentinel row must survive.
        await SqlServerDbConfigMigrator.MigrateAsync(_fixture.ConnectionString, schema: "configuration", ct);

        (await TableExistsAsync(_fixture.ConnectionString, "ConfigEntries", ct)).ShouldBeTrue();
        (await RowExistsAsync(_fixture.ConnectionString, sentinelId, ct)).ShouldBeTrue(
            "re-applying the migrator must preserve existing data — guard CREATEs with IF NOT EXISTS, never DROP+CREATE");
    }

    private static async Task InsertSentinelAsync(string connectionString, Guid id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO configuration.ConfigEntries (Id, Scope, Environment, TenantId, [Key], IsSecret, ModifiedUtc)
            VALUES (@id, 'sentinel', 'sentinel', '', 'sentinel-key', 0, SYSDATETIMEOFFSET())
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = id;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> RowExistsAsync(string connectionString, Guid id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM configuration.ConfigEntries WHERE Id = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = id;
        cmd.Parameters.Add(p);
        var count = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);

        return count > 0;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name = @n AND s.name = 'configuration'
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "@n";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        var result = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);

        return result > 0;
    }

    private static async Task DropDbConfigTablesAsync(string connectionString, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID('configuration.AuditEntries', 'U') IS NOT NULL DROP TABLE configuration.AuditEntries;
            IF OBJECT_ID('configuration.ConfigEntries', 'U') IS NOT NULL DROP TABLE configuration.ConfigEntries;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
