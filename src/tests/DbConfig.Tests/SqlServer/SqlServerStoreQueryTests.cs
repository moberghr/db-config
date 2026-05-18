using System.Text;
using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerStoreQueryTests : IAsyncLifetime
{
    private const string Env = "Production";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigStore _store = null!;

    public SqlServerStoreQueryTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
        _store = new EfCoreConfigStore(_fixture.DbContextFactory, new SqlServerUniqueConstraintDetector(), TimeProvider.System);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [TimedFact(30_000)]
    public async Task QueryAsync_NullFilters_ReturnsAllEntries_UsingServerSidePaging()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "K1", "v1", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry("AppB", "Staging", string.Empty, "K2", "v2", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry("AppA", Env, "Acme", "K3", "v3", false, t, null), CancellationToken.None);

        var sqlLog = new StringBuilder();
        var loggingStore = BuildLoggingStore(sqlLog);

        var result = await loggingStore.QueryAsync(null, null, null, null, 1000, CancellationToken.None);

        result.Count.ShouldBe(3);

        // The TOP clause must be present so paging happens server-side, not in-memory.
        var captured = sqlLog.ToString();
        captured.ShouldContain("TOP(", Case.Insensitive);
        captured.ShouldContain("DbConfig_Entries", Case.Insensitive);
    }

    [TimedFact(30_000)]
    public async Task QueryAsync_FilterByAppNameAndKeyPrefix_ReturnsMatchingRows()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Stripe:ApiKey", "v1", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Stripe:Currency", "v2", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Other:Key", "v3", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry("AppB", Env, string.Empty, "Stripe:ApiKey", "v4", false, t, null), CancellationToken.None);

        var result = await _store.QueryAsync("AppA", null, null, "Stripe:", 1000, CancellationToken.None);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(e => e.AppName == "AppA");
        result.ShouldAllBe(e => e.Key.StartsWith("Stripe:", StringComparison.Ordinal));
    }

    [TimedFact(30_000)]
    public async Task QueryAsync_TakeClampsResultCount()
    {
        var t = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await _store.UpsertAsync(
                new ConfigEntry("AppA", Env, string.Empty, $"Key{i:D2}", $"v{i}", false, t, null),
                CancellationToken.None);
        }

        var result = await _store.QueryAsync(null, null, null, null, 3, CancellationToken.None);

        result.Count.ShouldBe(3);
    }

    [TimedFact(30_000)]
    public async Task QueryAsync_FilterByTenantId_CaseSensitive()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry("AppA", Env, "Acme", "K1", "v1", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry("AppA", Env, "acme", "K2", "v2", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "K3", "v3", false, t, null), CancellationToken.None);

        var result = await _store.QueryAsync(null, null, "Acme", null, 1000, CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].TenantId.ShouldBe("Acme");
        result[0].Key.ShouldBe("K1");
    }

    private EfCoreConfigStore BuildLoggingStore(StringBuilder sqlLog)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DbConfigDbContext>(options =>
            options.UseSqlServer(
                _fixture.ConnectionString,
                sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer"))
            .LogTo(msg => sqlLog.AppendLine(msg), Microsoft.Extensions.Logging.LogLevel.Information));

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<DbConfigDbContext>>();

        return new EfCoreConfigStore(factory, new SqlServerUniqueConstraintDetector(), TimeProvider.System);
    }
}
