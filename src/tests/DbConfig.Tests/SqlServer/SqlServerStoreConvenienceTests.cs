using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// End-to-end smoke tests for the v0.11.1 convenience overloads on the EF Core store
/// against a real SQL Server container.
/// </summary>
[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerStoreConvenienceTests : IAsyncLifetime
{
    private const string App = "ConvenienceApp";
    private const string Env = "Test";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigStore _store = null!;
    private DbConfigOptions _options = null!;
    private MutableTenantResolver _resolver = null!;

    public SqlServerStoreConvenienceTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
        _options = new DbConfigOptions
        {
            Scope = App,
            Environment = Env,
        };
        _resolver = new MutableTenantResolver();
        _store = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new SqlServerUniqueConstraintDetector(),
            TimeProvider.System,
            _options,
            encryptor: null,
            enableAuditLog: true,
            tenantResolver: _resolver);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(30_000)]
    public async Task GetAsync_ImplicitAppEnv_ReturnsGlobalEntry()
    {
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Logging:Level", "Warning", false, DateTimeOffset.UtcNow, "seed"),
            CancellationToken.None);

        var result = await _store.GetAsync("Logging:Level", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe("Warning");
    }

    [TimedFact(30_000)]
    public async Task GetForTenantAsync_Typed_BindsTenantOverGlobal()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "TestOptions:ApiKey", "global", false, t, "seed"),
            CancellationToken.None);
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "TestOptions:WebhookSecret", "global-webhook", false, t, "seed"),
            CancellationToken.None);
        await _store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Acme", "TestOptions:ApiKey", "acme", false, t, "seed"),
            CancellationToken.None);

        var result = await _store.GetForTenantAsync<TestOptions>("Acme", CancellationToken.None);

        result.ApiKey.ShouldBe("acme");
        result.WebhookSecret.ShouldBe("global-webhook");
    }

    private sealed class TestOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        public string WebhookSecret { get; set; } = string.Empty;
    }
}
