using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.PostgreSql;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// Verifies that AppName, Environment, and Key columns use case-sensitive "C" collation
/// on PostgreSQL after the B25 migration.
/// A query using a wrong casing must return zero rows; an exact-case query must match.
/// </summary>
[Trait("Category", "PostgreSql")]
[Collection(PostgreSqlFixture.CollectionName)]
public sealed class PostgreSqlCollationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private EfCoreConfigStore _store = null!;

    public PostgreSqlCollationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
        _store = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new PostgreSqlUniqueConstraintDetector(),
            TimeProvider.System,
            _fixture.Encryptor,
            enableAuditLog: false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(30_000)]
    public async Task Entries_AppName_CaseSensitive_DifferentCaseReturnsNoRows()
    {
        await _store.UpsertAsync(
            new ConfigEntry("MyApp", "Production", string.Empty, "Key1", "value", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await ctx.ConfigEntries
            .AsNoTracking()
            .Where(e => e.AppName == "myapp" && e.Environment == "Production")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(0);
    }

    [TimedFact(30_000)]
    public async Task Entries_AppName_CaseSensitive_ExactCaseReturnsRow()
    {
        await _store.UpsertAsync(
            new ConfigEntry("MyApp", "Production", string.Empty, "Key2", "value", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await ctx.ConfigEntries
            .AsNoTracking()
            .Where(e => e.AppName == "MyApp" && e.Environment == "Production")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(1);
    }

    [TimedFact(30_000)]
    public async Task Entries_Environment_CaseSensitive_DifferentCaseReturnsNoRows()
    {
        await _store.UpsertAsync(
            new ConfigEntry("EnvApp", "Production", string.Empty, "Key3", "value", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await ctx.ConfigEntries
            .AsNoTracking()
            .Where(e => e.AppName == "EnvApp" && e.Environment == "production")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(0);
    }

    [TimedFact(30_000)]
    public async Task Entries_Environment_CaseSensitive_ExactCaseReturnsRow()
    {
        await _store.UpsertAsync(
            new ConfigEntry("EnvApp", "Production", string.Empty, "Key4", "value", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await ctx.ConfigEntries
            .AsNoTracking()
            .Where(e => e.AppName == "EnvApp" && e.Environment == "Production")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(1);
    }

    [TimedFact(30_000)]
    public async Task Entries_Key_CaseSensitive_DifferentCaseReturnsNoRows()
    {
        await _store.UpsertAsync(
            new ConfigEntry("KeyApp", "Production", string.Empty, "MySection:MyKey", "value", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await ctx.ConfigEntries
            .AsNoTracking()
            .Where(e => e.AppName == "KeyApp" && e.Environment == "Production" && e.Key == "mysection:mykey")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(0);
    }

    [TimedFact(30_000)]
    public async Task Entries_Key_CaseSensitive_ExactCaseReturnsRow()
    {
        await _store.UpsertAsync(
            new ConfigEntry("KeyApp", "Production", string.Empty, "MySection:MyKey", "value", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await ctx.ConfigEntries
            .AsNoTracking()
            .Where(e => e.AppName == "KeyApp" && e.Environment == "Production" && e.Key == "MySection:MyKey")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(1);
    }

    [TimedFact(30_000)]
    public async Task AuditEntries_AppName_CaseSensitive_DifferentCaseReturnsNoRows()
    {
        var storeWithAudit = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new PostgreSqlUniqueConstraintDetector(),
            TimeProvider.System,
            _fixture.Encryptor,
            enableAuditLog: true);

        await storeWithAudit.UpsertAsync(
            new ConfigEntry("AuditMyApp", "Production", string.Empty, "AuditKey1", "v", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await ctx.AuditEntries
            .AsNoTracking()
            .Where(e => e.AppName == "auditmyapp")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(0);
    }

    [TimedFact(30_000)]
    public async Task AuditEntries_AppName_CaseSensitive_ExactCaseReturnsRow()
    {
        var storeWithAudit = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new PostgreSqlUniqueConstraintDetector(),
            TimeProvider.System,
            _fixture.Encryptor,
            enableAuditLog: true);

        await storeWithAudit.UpsertAsync(
            new ConfigEntry("AuditMyApp", "Production", string.Empty, "AuditKey2", "v", false, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var count = await ctx.AuditEntries
            .AsNoTracking()
            .Where(e => e.AppName == "AuditMyApp")
            .CountAsync(CancellationToken.None);

        count.ShouldBe(1);
    }
}
