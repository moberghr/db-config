using DbConfig.Core;
using DbConfig.Provider.PostgreSql;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// The <see cref="NpgsqlDataSource"/> overloads: a host that builds its own data source (an Entra ID
/// password provider, custom TLS, Aspire's <c>AddAzureNpgsqlDataSource</c>) hands it to DbConfig and every
/// connection — migrator, polling provider, HTTP store — is opened from it.
/// </summary>
[Collection(PostgreSqlFixture.CollectionName)]
public sealed class PostgreSqlDataSourceTests : IAsyncLifetime
{
    private const string App = "DataSourceTestApp";
    private const string Env = "Test";

    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlDataSourceTests(PostgreSqlFixture fixture)
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
        // Restore the schema so the rest of the PostgreSql collection, which expects the tables to exist,
        // keeps working.
        await PostgreSqlDbConfigMigrator.MigrateAsync(
            _fixture.ConnectionString, schema: "configuration", CancellationToken.None);
    }

    [TimedFact(30_000)]
    public async Task MigrateAsync_DataSource_AppliesScript_AndIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeFalse();

        await PostgreSqlDbConfigMigrator.MigrateAsync(dataSource, schema: "configuration", ct);

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(_fixture.ConnectionString, "audit_entries", ct)).ShouldBeTrue();

        // Re-apply must be a no-op, not a failure.
        await PostgreSqlDbConfigMigrator.MigrateAsync(dataSource, schema: "configuration", ct);

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeTrue();
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_DataSource_CreateIfMissing_MigratesAndRoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeFalse();

        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.UsePostgreSql(dataSource);
        });

        // The startup migrator ran over the data source.
        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeTrue();

        using var host = builder.Build();
        var store = host.Services.GetRequiredService<IConfigStore>();
        var entry = new ConfigEntryRecord(App, Env, string.Empty, "Feature:Flag", "on", false, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(entry, ct);

        // Written and read back through the HTTP-side store, whose context factory was built over the
        // data source. The polling side is covered above: its first load ran inside AddDbConfig, and
        // NoneMode below shows it fails when that load cannot find the tables.
        var stored = await store.GetAsync(App, Env, "Feature:Flag", ct);
        stored.ShouldNotBeNull();
        stored.Value.ShouldBe("on");
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_DataSource_NoneMode_ThrowsWhenSchemaIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        var builder = Host.CreateApplicationBuilder();
        var exception = Record.Exception(() => builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.Options.SchemaMode = SchemaMode.None;
            b.UsePostgreSql(dataSource);
        }));

        // Same contract as the connection-string overload: the first load fails loudly, nothing is created.
        exception.ShouldBeOfType<InvalidOperationException>();
        (await TableExistsAsync(_fixture.ConnectionString, "config_entries", ct)).ShouldBeFalse();
    }

    [TimedFact]
    public void UsePostgreSql_NullDataSource_Throws()
    {
        var builder = Host.CreateApplicationBuilder();

        var exception = Record.Exception(() => builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.UsePostgreSql((NpgsqlDataSource)null!);
        }));

        exception.ShouldBeOfType<ArgumentNullException>();
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
