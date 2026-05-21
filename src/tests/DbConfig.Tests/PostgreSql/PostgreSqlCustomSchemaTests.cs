using DbConfig.Core;
using DbConfig.Provider.PostgreSql;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// Isolated test for the v0.13.0 configurable-schema feature on PostgreSQL. Spins up its
/// own Postgres container so it can verify a non-default schema name without competing
/// with the shared <see cref="PostgreSqlFixture"/> (which is pinned to the "configuration"
/// schema). Also covers <c>Schema=null</c> (database-default schema, i.e. <c>public</c>).
/// </summary>
[Trait("Category", "PostgreSql")]
public sealed class PostgreSqlCustomSchemaTests : IAsyncLifetime
{
    private const string App = "CustomSchemaApp";
    private const string Env = "Test";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_CustomSchema_CreatesTablesInThatSchema_AndRoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = _container.GetConnectionString();
        const string customSchema = "app_config";

        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.Options.Schema = customSchema;
            b.UsePostgreSql(connectionString);
        });

        using var host = builder.Build();

        // PG tables are snake_case; the schema must be the one we configured.
        (await TableExistsAsync(connectionString, customSchema, "config_entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(connectionString, customSchema, "audit_entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(connectionString, "configuration", "config_entries", ct)).ShouldBeFalse();
        (await TableExistsAsync(connectionString, "public", "config_entries", ct)).ShouldBeFalse();

        // Round-trip a value through the runtime store. Exercises Upsert, Get, and Delete —
        // all three must hit config_entries and audit_entries (audit write is in-transaction
        // per §0.7) in the custom schema.
        var store = host.Services.GetRequiredService<IConfigStore>();
        var entry = new ConfigEntryRecord(App, Env, string.Empty, "MyKey", "my-value", false, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(entry, ct);

        var fetched = await store.GetAsync(App, Env, "MyKey", ct);
        fetched.ShouldNotBeNull();
        fetched!.Value.ShouldBe("my-value");

        // The audit row must land in the custom-schema audit_entries table (§0.7).
        var auditCountAfterUpsert = await CountAuditRowsAsync(connectionString, customSchema, App, "MyKey", ct);
        auditCountAfterUpsert.ShouldBe(1L);

        await store.DeleteAsync(App, Env, "MyKey", ct);

        var afterDelete = await store.GetAsync(App, Env, "MyKey", ct);
        afterDelete.ShouldBeNull();

        // Delete writes a second audit row in the same custom-schema table.
        var auditCountAfterDelete = await CountAuditRowsAsync(connectionString, customSchema, App, "MyKey", ct);
        auditCountAfterDelete.ShouldBe(2L);
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_SchemaNull_UsesDatabaseDefaultPublicSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = _container.GetConnectionString();

        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.Options.Schema = null;
            b.UsePostgreSql(connectionString);
        });

        using var host = builder.Build();

        // Schema=null → PostgreSQL's default schema is "public".
        (await TableExistsAsync(connectionString, "public", "config_entries", ct)).ShouldBeTrue();
        (await TableExistsAsync(connectionString, "public", "audit_entries", ct)).ShouldBeTrue();

        var store = host.Services.GetRequiredService<IConfigStore>();
        var entry = new ConfigEntryRecord(App, Env, string.Empty, "NullSchemaKey", "v", false, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(entry, ct);

        var fetched = await store.GetAsync(App, Env, "NullSchemaKey", ct);
        fetched!.Value.ShouldBe("v");
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_CustomSchema_IConfigurationReadsValueAfterReload()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = _container.GetConnectionString();
        const string customSchema = "app_config";

        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.Options.Schema = customSchema;
            b.UsePostgreSql(connectionString);
        });

        using var host = builder.Build();

        var store = host.Services.GetRequiredService<IConfigStore>();
        var entry = new ConfigEntryRecord(App, Env, string.Empty, "MyKey", "configured-value", false, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(entry, ct);

        // Fire the reload signal so the polling provider picks up the write without waiting
        // for the 30-second default tick.
        var signal = host.Services.GetRequiredService<IDbConfigReloadSignal>();
        signal.Trigger();
        await Task.Delay(250, ct);

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        configuration["MyKey"].ShouldBe("configured-value");
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_CustomSchema_IsSecretEntry_RoundTripsThroughEncryptor()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = _container.GetConnectionString();
        const string customSchema = "secrets_schema";

        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.Options.Schema = customSchema;
            b.UsePostgreSql(connectionString);
        });

        using var host = builder.Build();
        var store = host.Services.GetRequiredService<IConfigStore>();

        // Upsert a secret. The store encrypts at write time using IConfigEncryptor.
        var secret = new ConfigEntryRecord(App, Env, string.Empty, "ApiKey", "sk_test_super_secret", IsSecret: true, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(secret, ct);

        // Verify the on-disk value is NOT plaintext (proves encryption ran in custom schema).
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT \"value\" FROM \"{customSchema}\".\"config_entries\" WHERE \"key\" = 'ApiKey'";
        var raw = (string?)await cmd.ExecuteScalarAsync(ct);
        raw.ShouldNotBeNullOrEmpty();
        raw.ShouldNotBe("sk_test_super_secret", "stored value must be ciphertext, not plaintext");

        // Reading back through the store must decrypt and return the plaintext.
        var fetched = await store.GetAsync(App, Env, "ApiKey", ct);
        fetched!.Value.ShouldBe("sk_test_super_secret");
        fetched.IsSecret.ShouldBeTrue();
    }

    private static async Task<long> CountAuditRowsAsync(
        string connectionString, string schema, string scope, string key, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{schema}\".\"audit_entries\" WHERE \"scope\" = @scope AND \"key\" = @key";
        var ps = cmd.CreateParameter();
        ps.ParameterName = "@scope";
        ps.Value = scope;
        cmd.Parameters.Add(ps);
        var pk = cmd.CreateParameter();
        pk.ParameterName = "@key";
        pk.Value = key;
        cmd.Parameters.Add(pk);

        return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string schema, string tableName, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = @s AND table_name = @n
            """;
        var pn = cmd.CreateParameter();
        pn.ParameterName = "@n";
        pn.Value = tableName;
        cmd.Parameters.Add(pn);
        var ps = cmd.CreateParameter();
        ps.ParameterName = "@s";
        ps.Value = schema;
        cmd.Parameters.Add(ps);

        var result = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);

        return result > 0;
    }
}
