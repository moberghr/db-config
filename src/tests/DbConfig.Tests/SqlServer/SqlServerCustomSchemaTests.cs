using DbConfig.Core;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Testcontainers.MsSql;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Isolated test for the v0.13.0 configurable-schema feature on SQL Server. Spins up its
/// own MsSql container so it can verify a non-default schema name without competing with
/// the shared <see cref="SqlServerFixture"/> (which is pinned to the "configuration" schema).
/// Also covers <c>Schema=null</c> (database-default schema, i.e. <c>dbo</c>) in the same
/// fixture to keep the container reuse cheap.
/// </summary>
[Trait("Category", "SqlServer")]
public sealed class SqlServerCustomSchemaTests : IAsyncLifetime
{
    private const string App = "CustomSchemaApp";
    private const string Env = "Test";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
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
            b.UseSqlServer(connectionString);
        });

        using var host = builder.Build();

        // The two tables MUST exist under the custom schema, not under "configuration" or "dbo".
        (await TableExistsAsync(connectionString, customSchema, "ConfigEntries", ct)).ShouldBeTrue();
        (await TableExistsAsync(connectionString, customSchema, "AuditEntries", ct)).ShouldBeTrue();
        (await TableExistsAsync(connectionString, "configuration", "ConfigEntries", ct)).ShouldBeFalse();
        (await TableExistsAsync(connectionString, "dbo", "ConfigEntries", ct)).ShouldBeFalse();

        // Round-trip a value through the runtime store to prove the runtime model also points
        // at the custom schema (not just the migration). Exercises Upsert, Get, and Delete —
        // all three must hit ConfigEntries and AuditEntries (audit write is in-transaction
        // per §0.7) in the custom schema.
        var store = host.Services.GetRequiredService<IConfigStore>();
        var entry = new ConfigEntryRecord(App, Env, string.Empty, "MyKey", "my-value", false, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(entry, ct);

        var fetched = await store.GetAsync(App, Env, "MyKey", ct);
        fetched.ShouldNotBeNull();
        fetched!.Value.ShouldBe("my-value");

        // The audit row must land in the custom-schema AuditEntries table (§0.7 — audit writes
        // are in-transaction with mutations). Verifies HasDefaultSchema applied to BOTH entities.
        var auditCountAfterUpsert = await CountAuditRowsAsync(connectionString, customSchema, App, "MyKey", ct);
        auditCountAfterUpsert.ShouldBe(1);

        await store.DeleteAsync(App, Env, "MyKey", ct);

        var afterDelete = await store.GetAsync(App, Env, "MyKey", ct);
        afterDelete.ShouldBeNull();

        // Delete writes a second audit row in the same custom-schema table.
        var auditCountAfterDelete = await CountAuditRowsAsync(connectionString, customSchema, App, "MyKey", ct);
        auditCountAfterDelete.ShouldBe(2);
    }

    [TimedFact(60_000)]
    public async Task AddDbConfig_SchemaNull_UsesDatabaseDefaultDboSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = _container.GetConnectionString();

        var builder = Host.CreateApplicationBuilder();
        builder.AddDbConfig(b =>
        {
            b.Options.Scope = App;
            b.Options.Environment = Env;
            b.Options.Schema = null;
            b.UseSqlServer(connectionString);
        });

        using var host = builder.Build();

        // Schema=null → SQL Server's default schema is "dbo".
        (await TableExistsAsync(connectionString, "dbo", "ConfigEntries", ct)).ShouldBeTrue();
        (await TableExistsAsync(connectionString, "dbo", "AuditEntries", ct)).ShouldBeTrue();

        // Round-trip through the runtime store.
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
            b.UseSqlServer(connectionString);
        });

        using var host = builder.Build();

        var store = host.Services.GetRequiredService<IConfigStore>();
        var entry = new ConfigEntryRecord(App, Env, string.Empty, "MyKey", "configured-value", false, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(entry, ct);

        // The HTTP write path fires the reload signal, but the store layer alone doesn't —
        // trigger it manually so the polling provider picks up the write without waiting
        // for the 30-second default tick.
        var signal = host.Services.GetRequiredService<IDbConfigReloadSignal>();
        signal.Trigger();

        // Reload is fire-and-forget; give the polling task a brief window to refresh the dictionary.
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
            b.UseSqlServer(connectionString);
        });

        using var host = builder.Build();
        var store = host.Services.GetRequiredService<IConfigStore>();

        // Upsert a secret. The store encrypts at write time using IConfigEncryptor.
        var secret = new ConfigEntryRecord(App, Env, string.Empty, "ApiKey", "sk_test_super_secret", IsSecret: true, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(secret, ct);

        // Verify the on-disk value is NOT plaintext (proves encryption ran in custom schema).
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [Value] FROM [{customSchema}].[ConfigEntries] WHERE [Key] = 'ApiKey'";
        var raw = (string?)await cmd.ExecuteScalarAsync(ct);
        raw.ShouldNotBeNullOrEmpty();
        raw.ShouldNotBe("sk_test_super_secret", "stored value must be ciphertext, not plaintext");

        // Reading back through the store must decrypt and return the plaintext.
        var fetched = await store.GetAsync(App, Env, "ApiKey", ct);
        fetched!.Value.ShouldBe("sk_test_super_secret");
        fetched.IsSecret.ShouldBeTrue();
    }

    private static async Task<int> CountAuditRowsAsync(
        string connectionString, string schema, string scope, string key, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [{schema}].[AuditEntries] WHERE [Scope] = @scope AND [Key] = @key";
        var ps = cmd.CreateParameter();
        ps.ParameterName = "@scope";
        ps.Value = scope;
        cmd.Parameters.Add(ps);
        var pk = cmd.CreateParameter();
        pk.ParameterName = "@key";
        pk.Value = key;
        cmd.Parameters.Add(pk);

        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string schema, string tableName, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name = @n AND s.name = @s
            """;
        var pn = cmd.CreateParameter();
        pn.ParameterName = "@n";
        pn.Value = tableName;
        cmd.Parameters.Add(pn);
        var ps = cmd.CreateParameter();
        ps.ParameterName = "@s";
        ps.Value = schema;
        cmd.Parameters.Add(ps);

        var result = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);

        return result > 0;
    }
}
