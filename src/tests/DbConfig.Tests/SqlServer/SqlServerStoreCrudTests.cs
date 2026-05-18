using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerStoreCrudTests : IAsyncLifetime
{
    private const string App = "TestApp";
    private const string Env = "Production";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigStore _store = null!;

    public SqlServerStoreCrudTests(SqlServerFixture fixture)
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
    public async Task Upsert_InsertsNew_WhenKeyDoesNotExist()
    {
        var entry = new ConfigEntry(App, Env, string.Empty, "Section:Key", "value1", false, DateTimeOffset.UtcNow, "user1");

        await _store.UpsertAsync(entry, CancellationToken.None);

        var all = await _store.GetAllAsync(App, Env, CancellationToken.None);
        all.ShouldHaveSingleItem();
        var stored = all[0];
        stored.AppName.ShouldBe(App);
        stored.Environment.ShouldBe(Env);
        stored.Key.ShouldBe("Section:Key");
        stored.Value.ShouldBe("value1");
        stored.IsSecret.ShouldBeFalse();
        stored.ModifiedBy.ShouldBe("user1");
    }

    [TimedFact(30_000)]
    public async Task Upsert_UpdatesValue_WhenKeyExists()
    {
        var t0 = DateTimeOffset.UtcNow.AddSeconds(-5);
        var initial = new ConfigEntry(App, Env, string.Empty, "Key", "old", false, t0, null);
        await _store.UpsertAsync(initial, CancellationToken.None);

        var t1 = t0.AddSeconds(1);
        var updated = new ConfigEntry(App, Env, string.Empty, "Key", "new", false, t1, "updater");
        await _store.UpsertAsync(updated, CancellationToken.None);

        var all = await _store.GetAllAsync(App, Env, CancellationToken.None);
        all.ShouldHaveSingleItem();
        var stored = all[0];
        stored.Value.ShouldBe("new");
        stored.ModifiedBy.ShouldBe("updater");
        stored.ModifiedUtc.ShouldBeGreaterThan(t0);
    }

    [TimedFact(30_000)]
    public async Task Delete_RemovesRow()
    {
        var entry = new ConfigEntry(App, Env, string.Empty, "ToDelete", "v", false, DateTimeOffset.UtcNow, null);
        await _store.UpsertAsync(entry, CancellationToken.None);

        await _store.DeleteAsync(App, Env, "ToDelete", CancellationToken.None);

        var all = await _store.GetAllAsync(App, Env, CancellationToken.None);
        all.ShouldBeEmpty();
    }

    [TimedFact(30_000)]
    public async Task Delete_NoOp_WhenKeyDoesNotExist()
    {
        var exception = await Record.ExceptionAsync(
            () => _store.DeleteAsync(App, Env, "NonExistent", CancellationToken.None));

        exception.ShouldBeNull();

        var all = await _store.GetAllAsync(App, Env, CancellationToken.None);
        all.ShouldBeEmpty();
    }

    [TimedFact(30_000)]
    public async Task GetAllAsync_ScopedByAppEnv()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key1", "v1", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry("OtherApp", Env, string.Empty, "Key2", "v2", false, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, "OtherEnv", string.Empty, "Key3", "v3", false, t, null), CancellationToken.None);

        var results = await _store.GetAllAsync(App, Env, CancellationToken.None);

        results.ShouldHaveSingleItem();
        results[0].Key.ShouldBe("Key1");
    }

    [TimedFact(30_000)]
    public async Task GetLatestModifiedUtcAsync_ReturnsHighestWatermark()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "A", "a", false, t1, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "B", "b", false, t3, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "C", "c", false, t2, null), CancellationToken.None);

        var watermark = await _store.GetLatestModifiedUtcAsync(App, Env, CancellationToken.None);

        watermark.ShouldNotBeNull();
        watermark!.Value.ShouldBe(t3);
    }

    [TimedFact(30_000)]
    public async Task GetLatestModifiedUtcAsync_ReturnsNull_WhenNoEntries()
    {
        var watermark = await _store.GetLatestModifiedUtcAsync(App, Env, CancellationToken.None);

        watermark.ShouldBeNull();
    }

    [TimedFact(30_000)]
    public async Task Upsert_TwiceSameKey_LastWriterWins()
    {
        var t0 = DateTimeOffset.UtcNow;
        var first = new ConfigEntry(App, Env, string.Empty, "Concurrent", "first", false, t0, "writer1");
        var second = new ConfigEntry(App, Env, string.Empty, "Concurrent", "second", false, t0.AddMilliseconds(1), "writer2");

        await _store.UpsertAsync(first, CancellationToken.None);
        await _store.UpsertAsync(second, CancellationToken.None);

        var all = await _store.GetAllAsync(App, Env, CancellationToken.None);
        all.ShouldHaveSingleItem();
        all[0].Value.ShouldBe("second");
    }

    [TimedFact(30_000)]
    public async Task GetAllAsync_ReturnsSecretEntries()
    {
        var entry = new ConfigEntry(App, Env, string.Empty, "SecretKey", "s3cr3t", true, DateTimeOffset.UtcNow, null);
        await _store.UpsertAsync(entry, CancellationToken.None);

        var results = await _store.GetAllAsync(App, Env, CancellationToken.None);

        results.ShouldHaveSingleItem();
        results[0].IsSecret.ShouldBeTrue();
        results[0].Value.ShouldBe("s3cr3t");
    }

    [TimedFact(30_000)]
    public async Task Upsert_Concurrent_LastWriterWins_NoException()
    {
        var t = DateTimeOffset.UtcNow;
        var first = new ConfigEntry(App, Env, string.Empty, "RaceKey", "value-a", false, t, "writer-a");
        var second = new ConfigEntry(App, Env, string.Empty, "RaceKey", "value-b", false, t.AddMilliseconds(1), "writer-b");

        // Both tasks try to insert the same key simultaneously. With the retry logic in
        // EfCoreConfigStore one of them will hit a unique-constraint error and retry as an update.
        // Neither should throw; the final row must contain one of the two values.
        var exception = await Record.ExceptionAsync(() =>
            Task.WhenAll(
                _store.UpsertAsync(first, CancellationToken.None),
                _store.UpsertAsync(second, CancellationToken.None)));

        exception.ShouldBeNull();

        var all = await _store.GetAllAsync(App, Env, CancellationToken.None);
        all.ShouldHaveSingleItem();
        all[0].Value.ShouldBeOneOf("value-a", "value-b");
    }
}
