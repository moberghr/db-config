using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DbConfig.Tests.SqlServer;

[Trait("Category", "SqlServer")]
[Collection(SqlServerFixture.CollectionName)]
public sealed class SqlServerReloadIntegrationTests : IAsyncLifetime
{
    private const string App = "ReloadApp";
    private const string Env = "Test";

    private readonly SqlServerFixture _fixture;
    private EfCoreConfigStore _store = null!;

    public SqlServerReloadIntegrationTests(SqlServerFixture fixture)
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

    [TimedFact(60_000)]
    public async Task FullReloadFlow_WriteThenPoll_IConfigurationReflects()
    {
        // Arrange: seed an initial entry.
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await _store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "Section:Key", "initial", false, t0, null),
            TestContext.Current.CancellationToken);

        var fakeTime = new FakeTimeProvider();
        var reloadInterval = TimeSpan.FromSeconds(5);
        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = reloadInterval,
        };

        var source = new DbConfigConfigurationSource(options, _store, fakeTime, NullLoggerFactory.Instance);
        var configBuilder = new ConfigurationBuilder();
        configBuilder.Add(source);
        var configuration = configBuilder.Build();

        // Act part 1: initial load should have the seeded value.
        configuration["Section:Key"].ShouldBe("initial");

        // Act part 2: upsert a new value with an advanced watermark.
        var t1 = t0.AddSeconds(1);
        await _store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "Section:Key", "updated", false, t1, null),
            TestContext.Current.CancellationToken);

        // Arm a TCS on the reload token before advancing time so we don't miss the signal.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        configuration.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        // Act part 3: advance time to trigger the polling timer.
        fakeTime.Advance(reloadInterval);

        // Wait for the reload to complete (timer callback runs on thread pool).
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert.
        configuration["Section:Key"].ShouldBe("updated");
    }

    [TimedFact(60_000)]
    public async Task FullReloadFlow_DeletedKey_IConfigurationNoLongerContainsKey()
    {
        // Arrange: seed an initial entry.
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await _store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "ToRemove", "value", false, t0, null),
            TestContext.Current.CancellationToken);

        var fakeTime = new FakeTimeProvider();
        var reloadInterval = TimeSpan.FromSeconds(5);
        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = reloadInterval,
        };

        var source = new DbConfigConfigurationSource(options, _store, fakeTime, NullLoggerFactory.Instance);
        var configBuilder = new ConfigurationBuilder();
        configBuilder.Add(source);
        var configuration = configBuilder.Build();

        configuration["ToRemove"].ShouldBe("value");

        // Act: upsert a different key with a new watermark, then delete the original.
        // We need the watermark to advance so the provider reloads.
        var t1 = t0.AddSeconds(1);
        await _store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "AnotherKey", "x", false, t1, null),
            TestContext.Current.CancellationToken);
        await _store.DeleteAsync(App, Env, "ToRemove", TestContext.Current.CancellationToken);

        // Arm a TCS on the reload token before advancing time so we don't miss the signal.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        configuration.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        fakeTime.Advance(reloadInterval);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        configuration["ToRemove"].ShouldBeNull();
    }
}
