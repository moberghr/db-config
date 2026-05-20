using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Http;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

/// <summary>
/// Integration tests for the read-audit feature on SQL Server.
/// Verifies <see cref="EfCoreConfigAuditStore.WriteAsync"/> and the end-to-end HTTP GET → audit row flow.
/// </summary>
[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerReadAuditTests : IAsyncLifetime
{
    private const string App = "ReadAuditApp";
    private const string Env = "ReadAuditEnv";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigAuditStore _auditStore = null!;

    public SqlServerReadAuditTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
        _auditStore = new EfCoreConfigAuditStore(_fixture.DbContextFactory, _fixture.Encryptor);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(30_000)]
    public async Task EfCoreAuditStore_WriteAsync_PersistsRowToAuditTable()
    {
        var entry = new ConfigAuditEntry(
            Id: Guid.NewGuid(),
            Scope: App,
            Environment: Env,
            TenantId: string.Empty,
            Key: "ReadKey",
            OldValue: null,
            NewValue: null,
            IsSecret: false,
            Action: ConfigAuditAction.Read,
            ModifiedUtc: DateTimeOffset.UtcNow,
            ModifiedBy: "read-tester");

        await _auditStore.WriteAsync(entry, CancellationToken.None);

        var history = await _auditStore.GetHistoryAsync(App, Env, "ReadKey", 10, CancellationToken.None);

        history.ShouldHaveSingleItem();
        history[0].Action.ShouldBe(ConfigAuditAction.Read);
        history[0].OldValue.ShouldBeNull();
        history[0].NewValue.ShouldBeNull();
        history[0].ModifiedBy.ShouldBe("read-tester");
    }

    [TimedFact(30_000)]
    public async Task EfCoreAuditStore_WriteAsync_FireAndForget_NotInTransaction()
    {
        var entry = new ConfigAuditEntry(
            Id: Guid.NewGuid(),
            Scope: App,
            Environment: Env,
            TenantId: string.Empty,
            Key: "IndependentKey",
            OldValue: null,
            NewValue: null,
            IsSecret: false,
            Action: ConfigAuditAction.Read,
            ModifiedUtc: DateTimeOffset.UtcNow,
            ModifiedBy: null);

        await _auditStore.WriteAsync(entry, CancellationToken.None);

        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var raw = await context.AuditEntries
            .AsNoTracking()
            .Where(x => x.Scope == App && x.Environment == Env && x.Key == "IndependentKey")
            .FirstOrDefaultAsync(CancellationToken.None);

        raw.ShouldNotBeNull();
        raw!.Action.ShouldBe("Read");
        raw.OldValue.ShouldBeNull();
        raw.NewValue.ShouldBeNull();
    }

    [TimedFact(30_000)]
    public async Task EndToEnd_AuditReadsOn_HttpGet_WritesReadRowToAuditTable()
    {
        var store = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new SqlServerUniqueConstraintDetector(),
            TimeProvider.System,
            _fixture.Encryptor,
            enableAuditLog: true);

        await store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "E2EKey", "e2e-value", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        var auditStore = new EfCoreConfigAuditStore(_fixture.DbContextFactory, _fixture.Encryptor);
        var options = new DbConfigOptions { AuditReads = true };

        await using var app = BuildApp(store, auditStore, options);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/E2EKey",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        var found = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);

            await using var context = await _fixture.DbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            found = await context.AuditEntries
                .AsNoTracking()
                .AnyAsync(
                    x => x.Scope == App && x.Environment == Env && x.Key == "E2EKey" && x.Action == "Read",
                    TestContext.Current.CancellationToken);

            if (found)
            {
                break;
            }
        }

        found.ShouldBeTrue("Read audit row should have been written to the SQL Server audit table.");
    }

    private static WebApplication BuildApp(
        EfCoreConfigStore store,
        EfCoreConfigAuditStore auditStore,
        DbConfigOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore>(store);
        builder.Services.AddSingleton<IConfigAuditStore>(auditStore);
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(options);

        var app = builder.Build();
        app.MapDbConfigHttp("/api/dbconfig");

        return app;
    }

    private sealed class NoOpReloadSignal : IDbConfigReloadSignal
    {
        public void Trigger()
        {
        }
    }
}
