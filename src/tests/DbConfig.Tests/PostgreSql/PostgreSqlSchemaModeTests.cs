using DbConfig.Core;
using DbConfig.Provider.PostgreSql;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// Verifies <see cref="SchemaMode"/> behavior on PostgreSQL: CreateIfMissing
/// (the default) auto-applies the raw-SQL initial-create script during AddDbConfig;
/// None skips it entirely. Also exercises the <see cref="PostgreSqlDbConfigMigrator"/>
/// public surface (script content + idempotent execution).
/// </summary>
[Trait("Category", "PostgreSql")]
[Collection(PostgreSqlFixture.CollectionName)]
public sealed class PostgreSqlSchemaModeTests : IAsyncLifetime
{
    private const string App = "SchemaModeTestApp";
    private const string Env = "Test";

    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSchemaModeTests(PostgreSqlFixture fixture)
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
        // Restore schema after each test so other tests in the PostgreSql collection
        // that expect DbConfig tables to exist (most of them) keep working.
        await PostgreSqlDbConfigMigrator.MigrateAsync(
            _fixture.ConnectionString, schema: "configuration", CancellationToken.None);
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_CreateIfMissingMode_AutoAppliesMigrations()
    {
        var ct = TestContext.Current.CancellationToken;

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeFalse();

        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.UsePostgreSql(_fixture.ConnectionString);
        });

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "audit_entries", ct)).ShouldBeTrue();

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

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeFalse();

        var builder = Host.CreateApplicationBuilder();
        var exception = Record.Exception(() => builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.Options.SchemaMode = SchemaMode.None;
            b.UsePostgreSql(_fixture.ConnectionString);
        }));

        // The polling provider's Load() runs synchronously inside AddDbConfig and fails
        // because the table doesn't exist. The failure must surface as an InvalidOperationException
        // (wrapped by DbConfigConfigurationProvider — the raw provider exception is the inner one).
        exception.ShouldBeOfType<InvalidOperationException>();
        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeFalse();
    }

    [TimedFact]
    public void GetCreateScript_ContainsExpectedTablesAndIdempotencyGuards()
    {
        var sql = PostgreSqlDbConfigMigrator.GetCreateScript();

        sql.ShouldContain("config_entries");
        sql.ShouldContain("audit_entries");

        // Idempotency: every CREATE must use IF NOT EXISTS.
        sql.ShouldContain("IF NOT EXISTS");
    }

    [TimedFact]
    public void GetCreateScript_SubstitutesSchemaPlaceholder()
    {
        var defaultSchema = PostgreSqlDbConfigMigrator.GetCreateScript();
        var customSchema = PostgreSqlDbConfigMigrator.GetCreateScript("my_schema");

        defaultSchema.ShouldContain("configuration");
        defaultSchema.ShouldNotContain("{schema}");

        customSchema.ShouldContain("my_schema");
        customSchema.ShouldNotContain("{schema}");
        customSchema.ShouldNotContain("\"configuration\"");
    }

    [TimedFact(30_000)]
    public async Task MigrateAsync_AppliesScript_AndIsIdempotent_DataSurvivesReApply()
    {
        var ct = TestContext.Current.CancellationToken;
        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeFalse();

        await PostgreSqlDbConfigMigrator.MigrateAsync(_fixture.ConnectionString, schema: "configuration", ct);

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "audit_entries", ct)).ShouldBeTrue();

        // Insert a sentinel row to prove re-apply does NOT recreate tables.
        var sentinelId = Guid.NewGuid();
        await InsertSentinelAsync(_fixture.ConnectionString, sentinelId, ct);

        // Re-apply: must not throw, tables must still exist, sentinel must survive.
        await PostgreSqlDbConfigMigrator.MigrateAsync(_fixture.ConnectionString, schema: "configuration", ct);

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeTrue();
        (await RowExistsAsync(_fixture.ConnectionString, sentinelId, ct)).ShouldBeTrue(
            "re-applying the migrator must preserve existing data — guard CREATEs with IF NOT EXISTS, never DROP+CREATE");
    }

    private static async Task InsertSentinelAsync(string connectionString, Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO configuration.config_entries (id, scope, environment, tenant_id, key, is_secret, modified_utc)
            VALUES (@id, 'sentinel', 'sentinel', '', 'sentinel-key', false, NOW())
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = id;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> RowExistsAsync(string connectionString, Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM configuration.config_entries WHERE id = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = id;
        cmd.Parameters.Add(p);
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);

        return count > 0;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'configuration' AND table_name = @n
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "@n";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        var result = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);

        return result > 0;
    }

    private static async Task DropDbConfigTablesAsync(string connectionString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DROP TABLE IF EXISTS configuration.audit_entries CASCADE;
            DROP TABLE IF EXISTS configuration.config_entries CASCADE;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
