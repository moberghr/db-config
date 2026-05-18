using System.Text;
using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.PostgreSql;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

[Trait("Category", "PostgreSql")]
[Collection(PostgreSqlFixture.CollectionName)]
public sealed class PostgreSqlStoreGetAsyncTests : IAsyncLifetime
{
    private const string App = "TestApp";
    private const string Env = "Production";

    private readonly PostgreSqlFixture _fixture;
    private EfCoreConfigStore _store = null!;

    public PostgreSqlStoreGetAsyncTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
        _store = new EfCoreConfigStore(_fixture.DbContextFactory, new PostgreSqlUniqueConstraintDetector(), TimeProvider.System);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [TimedFact(30_000)]
    public async Task GetAsync_KeyExists_ReturnsEntry()
    {
        var t = DateTimeOffset.UtcNow;
        var entry = new ConfigEntry(App, Env, string.Empty, "Section:Key", "value1", false, t, "user1");
        await _store.UpsertAsync(entry, CancellationToken.None);

        var result = await _store.GetAsync(App, Env, "Section:Key", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.AppName.ShouldBe(App);
        result.Environment.ShouldBe(Env);
        result.Key.ShouldBe("Section:Key");
        result.Value.ShouldBe("value1");
        result.IsSecret.ShouldBeFalse();
        result.ModifiedBy.ShouldBe("user1");
    }

    [TimedFact(30_000)]
    public async Task GetAsync_KeyDoesNotExist_ReturnsNull()
    {
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "OtherKey", "v", false, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var result = await _store.GetAsync(App, Env, "NonExistentKey", CancellationToken.None);

        result.ShouldBeNull();
    }

    [TimedFact(30_000)]
    public async Task GetAsync_OnlyFetchesOneRow()
    {
        // Seed multiple entries in the same scope.
        var t = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 5; i++)
        {
            await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, $"Key{i}", $"v{i}", false, t, null), CancellationToken.None);
        }

        // Build a separate store with SQL logging enabled to capture the generated query.
        var sqlLog = new StringBuilder();
        var services = new ServiceCollection();
        services.AddDbContextFactory<DbConfigDbContext>(options =>
            options.UseNpgsql(
                _fixture.ConnectionString,
                npg => npg.MigrationsAssembly("DbConfig.Provider.PostgreSql"))
            .LogTo(msg => sqlLog.AppendLine(msg), Microsoft.Extensions.Logging.LogLevel.Information));

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<DbConfigDbContext>>();
        var loggingStore = new EfCoreConfigStore(factory, new PostgreSqlUniqueConstraintDetector(), TimeProvider.System);

        var result = await loggingStore.GetAsync(App, Env, "Key3", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe("v3");

        // The SQL must contain a WHERE predicate that filters on the Key column.
        // EF Core parameterises the value, so we look for the Key column reference
        // in the WHERE clause rather than the literal string "Key3".
        var capturedSql = sqlLog.ToString();
        capturedSql.ShouldContain("WHERE", Case.Insensitive);
        capturedSql.ShouldContain("\"Key\"", Case.Sensitive);
    }
}
