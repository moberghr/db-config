using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.PostgreSql;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// Verifies <see cref="SchemaMode"/> behavior on PostgreSQL: CreateIfMissing
/// (the default) auto-applies migrations during AddDbConfig; None skips them
/// entirely. Also exercises the <see cref="DbConfigMigrator"/> script generation
/// helpers.
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
        var opts = PostgreSqlDbConfigOptions.ForPostgreSql(_fixture.ConnectionString);
        await DbConfigMigrator.MigrateAsync(opts, CancellationToken.None);
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_CreateIfMissingMode_AutoAppliesMigrations()
    {
        var ct = TestContext.Current.CancellationToken;

        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeFalse();

        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.AppName = App;
            b.Options.Environment = Env;
            b.UsePostgreSql(_fixture.ConnectionString);
        });

        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_AuditEntries", ct)).ShouldBeTrue();

        using var host = builder.Build();
        var store = host.Services.GetRequiredService<IConfigStore>();
        var entry = new ConfigEntry(App, Env, string.Empty, "Key", "value", false, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(entry, ct);

        var all = await store.GetAllAsync(App, Env, ct);
        all.ShouldHaveSingleItem();
        all[0].Value.ShouldBe("value");
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_NoneMode_DoesNotApplyMigrations()
    {
        var ct = TestContext.Current.CancellationToken;

        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeFalse();

        var builder = Host.CreateApplicationBuilder();
        var exception = Record.Exception(() => builder.AddDbConfig(b =>
        {
            b.Options.AppName = App;
            b.Options.Environment = Env;
            b.Options.SchemaMode = SchemaMode.None;
            b.UsePostgreSql(_fixture.ConnectionString);
        }));

        exception.ShouldNotBeNull();
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeFalse();
    }

    [TimedFact(30_000)]
    public void DbConfigMigrator_GenerateCreateScript_ContainsExpectedTables()
    {
        var opts = PostgreSqlDbConfigOptions.ForPostgreSql(_fixture.ConnectionString);

        var sql = DbConfigMigrator.GenerateCreateScript(opts);

        sql.ShouldContain("DbConfig_Entries");
        sql.ShouldContain("DbConfig_AuditEntries");
    }

    [TimedFact(30_000)]
    public void DbConfigMigrator_GenerateMigrationScript_IsIdempotent()
    {
        var opts = PostgreSqlDbConfigOptions.ForPostgreSql(_fixture.ConnectionString);

        var sql = DbConfigMigrator.GenerateMigrationScript(opts, idempotent: true);

        // Npgsql idempotent migration scripts consult __EFMigrationsHistory before each migration.
        sql.ShouldContain("__EFMigrationsHistory");
        sql.ShouldContain("DbConfig_Entries");
    }

    [TimedFact(30_000)]
    public async Task DbConfigMigrator_MigrateAsync_AppliesPendingMigrations()
    {
        var ct = TestContext.Current.CancellationToken;
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeFalse();

        var opts = PostgreSqlDbConfigOptions.ForPostgreSql(_fixture.ConnectionString);
        await DbConfigMigrator.MigrateAsync(opts, ct);

        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_AuditEntries", ct)).ShouldBeTrue();
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @n";
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
            DROP TABLE IF EXISTS "DbConfig_AuditEntries" CASCADE;
            DROP TABLE IF EXISTS "DbConfig_Entries" CASCADE;
            DROP TABLE IF EXISTS "__EFMigrationsHistory" CASCADE;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
