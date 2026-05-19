using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Verifies <see cref="SchemaMode"/> behavior on SQL Server: CreateIfMissing
/// (the default) auto-applies migrations during AddDbConfig; None skips them
/// entirely. Also exercises the <see cref="DbConfigMigrator"/> script generation
/// helpers.
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
        // Restore schema after each test so other tests in the SqlServer collection
        // that expect DbConfig tables to exist (most of them) keep working.
        var opts = SqlServerDbConfigOptions.ForSqlServer(_fixture.ConnectionString);
        await DbConfigMigrator.MigrateAsync(opts, CancellationToken.None);
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_CreateIfMissingMode_AutoAppliesMigrations()
    {
        var ct = TestContext.Current.CancellationToken;

        // Pre-condition: tables do NOT exist after the drop in InitializeAsync.
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeFalse();

        // AddDbConfig with default SchemaMode = CreateIfMissing.
        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.AppName = App;
            b.Options.Environment = Env;
            b.UseSqlServer(_fixture.ConnectionString);
        });

        // Tables MUST exist after AddDbConfig returns.
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_AuditEntries", ct)).ShouldBeTrue();

        // A basic Upsert must round-trip through the now-ready schema.
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

        // AddDbConfig with SchemaMode.None must NOT migrate. The polling provider's
        // first Load() then fails because the table doesn't exist. We accept either
        // an exception at AddDbConfig time (from Load) or a clear failure later — what
        // we verify is that no tables were created.
        var builder = Host.CreateApplicationBuilder();
        var exception = Record.Exception(() => builder.AddDbConfig(b =>
        {
            b.Options.AppName = App;
            b.Options.Environment = Env;
            b.Options.SchemaMode = SchemaMode.None;
            b.UseSqlServer(_fixture.ConnectionString);
        }));

        // Load() throws because DbConfig_Entries doesn't exist.
        exception.ShouldNotBeNull();
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeFalse();
    }

    [TimedFact(30_000)]
    public void DbConfigMigrator_GenerateCreateScript_ContainsExpectedTables()
    {
        var opts = SqlServerDbConfigOptions.ForSqlServer(_fixture.ConnectionString);

        var sql = DbConfigMigrator.GenerateCreateScript(opts);

        sql.ShouldContain("DbConfig_Entries");
        sql.ShouldContain("DbConfig_AuditEntries");
    }

    [TimedFact(30_000)]
    public void DbConfigMigrator_GenerateMigrationScript_IsIdempotent()
    {
        var opts = SqlServerDbConfigOptions.ForSqlServer(_fixture.ConnectionString);

        var sql = DbConfigMigrator.GenerateMigrationScript(opts, idempotent: true);

        // Idempotent SQL Server scripts query __EFMigrationsHistory before applying each migration.
        sql.ShouldContain("__EFMigrationsHistory");
        sql.ShouldContain("DbConfig_Entries");
    }

    [TimedFact(30_000)]
    public async Task DbConfigMigrator_MigrateAsync_AppliesPendingMigrations()
    {
        var ct = TestContext.Current.CancellationToken;
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeFalse();

        var opts = SqlServerDbConfigOptions.ForSqlServer(_fixture.ConnectionString);
        await DbConfigMigrator.MigrateAsync(opts, ct);

        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_Entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "DbConfig_AuditEntries", ct)).ShouldBeTrue();
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @n";
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
            IF OBJECT_ID('dbo.DbConfig_AuditEntries', 'U') IS NOT NULL DROP TABLE dbo.DbConfig_AuditEntries;
            IF OBJECT_ID('dbo.DbConfig_Entries', 'U') IS NOT NULL DROP TABLE dbo.DbConfig_Entries;
            IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NOT NULL DROP TABLE dbo.__EFMigrationsHistory;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
