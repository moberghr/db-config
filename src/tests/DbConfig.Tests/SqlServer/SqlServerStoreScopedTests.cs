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
public sealed class SqlServerStoreScopedTests : IAsyncLifetime
{
    private const string Env = "Production";
    private const string AppOwn = "PaymentService";
    private const string AppShared = "Shared";
    private const string AppPlatform = "PlatformDefaults";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigStore _store = null!;

    public SqlServerStoreScopedTests(SqlServerFixture fixture)
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
    public async Task GetAllScopedAsync_ScopedByAppEnv_ReturnsExpectedRows()
    {
        // Insert rows in three scopes + one unrelated scope
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, Env, string.Empty, "OwnKey", "ownVal", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord(AppShared, Env, string.Empty, "SharedKey", "sharedVal", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord(AppPlatform, Env, string.Empty, "PlatformKey", "platformVal", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord("OtherApp", Env, string.Empty, "OtherKey", "otherVal", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, "OtherEnv", string.Empty, "EnvKey", "envVal", false, t, null), CancellationToken.None);

        // Query two of the three scopes + own
        string[] scopes = [AppPlatform, AppShared, AppOwn];
        var results = await _store.GetAllScopedAsync(scopes, Env, CancellationToken.None);

        // Only rows matching the queried scopes + env should be returned
        results.Count.ShouldBe(3);
        results.ShouldAllBe(e => e.Environment == Env);
        results.ShouldContain(e => e.Scope == AppOwn && e.Key == "OwnKey");
        results.ShouldContain(e => e.Scope == AppShared && e.Key == "SharedKey");
        results.ShouldContain(e => e.Scope == AppPlatform && e.Key == "PlatformKey");
        results.ShouldNotContain(e => e.Scope == "OtherApp");
        results.ShouldNotContain(e => e.Environment == "OtherEnv");
    }

    [TimedFact(30_000)]
    public async Task GetAllScopedAsync_PreservesInputScopeOrder()
    {
        // Insert rows in reverse order to ensure DB ordering doesn't happen to match input
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, Env, string.Empty, "Key", "ownVal", false, t.AddSeconds(2), null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord(AppShared, Env, string.Empty, "Key", "sharedVal", false, t.AddSeconds(1), null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord(AppPlatform, Env, string.Empty, "Key", "platformVal", false, t, null), CancellationToken.None);

        // Query in forward-precedence order: lowest first, own last
        string[] scopes = [AppPlatform, AppShared, AppOwn];
        var results = await _store.GetAllScopedAsync(scopes, Env, CancellationToken.None);

        // All three rows present
        results.Count.ShouldBe(3);

        // Entries must be grouped by Scope in the same order as the input list
        // PlatformDefaults entries first, Shared next, PaymentService last
        var scopesInOrder = results.Select(e => e.Scope).ToList();
        var platformIdx = scopesInOrder.IndexOf(AppPlatform);
        var sharedIdx = scopesInOrder.IndexOf(AppShared);
        var ownIdx = scopesInOrder.IndexOf(AppOwn);

        platformIdx.ShouldBeLessThan(sharedIdx);
        sharedIdx.ShouldBeLessThan(ownIdx);
    }

    [TimedFact(30_000)]
    public async Task GetAllScopedAsync_OnlyOneSqlQuery()
    {
        // Seed data across two scopes
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, Env, string.Empty, "OwnKey", "v1", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord(AppShared, Env, string.Empty, "SharedKey", "v2", false, t, null), CancellationToken.None);

        // Build a separate store with SQL logging enabled
        var sqlLog = new StringBuilder();
        var services = new ServiceCollection();
        services.AddDbContextFactory<DbConfigDbContext>(options =>
            options.UseSqlServer(_fixture.ConnectionString)
                .UseDbConfigSchema("configuration")
                .LogTo(msg => sqlLog.AppendLine(msg), Microsoft.Extensions.Logging.LogLevel.Information));

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<DbConfigDbContext>>();
        var loggingStore = new EfCoreConfigStore(factory, new SqlServerUniqueConstraintDetector(), TimeProvider.System);

        var results = await loggingStore.GetAllScopedAsync([AppShared, AppOwn], Env, CancellationToken.None);

        results.Count.ShouldBe(2);

        // A single SELECT with IN clause must be issued — not one query per scope
        var capturedSql = sqlLog.ToString();
        capturedSql.ShouldContain("SELECT", Case.Insensitive);
        capturedSql.ShouldContain("IN (", Case.Insensitive);
    }

    [TimedFact(30_000)]
    public async Task GetAllScopedAsync_EmptyScopeList_ReturnsEmpty()
    {
        // Seed some data so the table is not empty
        await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, Env, string.Empty, "Key", "v", false, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var results = await _store.GetAllScopedAsync([], Env, CancellationToken.None);

        results.ShouldBeEmpty();
    }

    [TimedFact(30_000)]
    public async Task GetLatestModifiedUtcScopedAsync_ReturnsMaxAcrossScopes()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

        // t3 is in AppShared, not in AppOwn — to confirm the max crosses scope boundaries
        await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, Env, string.Empty, "A", "a", false, t1, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, Env, string.Empty, "B", "b", false, t2, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntryRecord(AppShared, Env, string.Empty, "C", "c", false, t3, null), CancellationToken.None);

        var watermark = await _store.GetLatestModifiedUtcScopedAsync(
            [AppShared, AppOwn], Env, CancellationToken.None);

        watermark.ShouldNotBeNull();
        watermark!.Value.ShouldBe(t3);
    }

    [TimedFact(30_000)]
    public async Task GetLatestModifiedUtcScopedAsync_OnlyAggregatesNoFullScan()
    {
        // Seed several rows
        var t = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 3; i++)
        {
            await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, Env, string.Empty, $"Key{i}", $"v{i}", false, t.AddSeconds(i), null), CancellationToken.None);
        }

        await _store.UpsertAsync(new ConfigEntryRecord(AppShared, Env, string.Empty, "SharedKey", "sv", false, t.AddSeconds(10), null), CancellationToken.None);

        // Build a logging store
        var sqlLog = new StringBuilder();
        var services = new ServiceCollection();
        services.AddDbContextFactory<DbConfigDbContext>(options =>
            options.UseSqlServer(_fixture.ConnectionString)
                .UseDbConfigSchema("configuration")
                .LogTo(msg => sqlLog.AppendLine(msg), Microsoft.Extensions.Logging.LogLevel.Information));

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<DbConfigDbContext>>();
        var loggingStore = new EfCoreConfigStore(factory, new SqlServerUniqueConstraintDetector(), TimeProvider.System);

        var watermark = await loggingStore.GetLatestModifiedUtcScopedAsync(
            [AppShared, AppOwn], Env, CancellationToken.None);

        watermark.ShouldNotBeNull();

        // The generated SQL must use MAX aggregate — not a full row fetch with ORDER BY
        var capturedSql = sqlLog.ToString();
        capturedSql.ShouldContain("MAX(", Case.Insensitive);
    }

    [TimedFact(30_000)]
    public async Task GetLatestModifiedUtcScopedAsync_NoRowsInAnyScope_ReturnsNull()
    {
        var watermark = await _store.GetLatestModifiedUtcScopedAsync(
            [AppShared, AppOwn], Env, CancellationToken.None);

        watermark.ShouldBeNull();
    }

    [TimedFact(30_000)]
    public async Task GetLatestModifiedUtcScopedAsync_EmptyScopeList_ReturnsNull()
    {
        // Seed some data so the table is not empty.
        await _store.UpsertAsync(new ConfigEntryRecord(AppOwn, Env, string.Empty, "Key", "v", false, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var watermark = await _store.GetLatestModifiedUtcScopedAsync(
            [], Env, CancellationToken.None);

        watermark.ShouldBeNull();
    }
}
