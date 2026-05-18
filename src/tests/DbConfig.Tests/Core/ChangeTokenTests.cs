using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class ChangeTokenTests
{
    private const string App = "TestApp";
    private const string Env = "Test";

    [TimedFact]
    public void GetReloadToken_BeforeReload_IsNotExpired()
    {
        var store = new InMemoryConfigStore();
        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
        };

        var provider = new DbConfigConfigurationProvider(options, store, TimeProvider.System, NullLoggerFactory.Instance);
        provider.Load();

        var token = provider.GetReloadToken();

        token.ShouldNotBeNull();
        token.HasChanged.ShouldBeFalse();
    }

    [TimedFact]
    public async Task ChangeToken_FiresWhenWatermarkAdvances()
    {
        var store = new InMemoryConfigStore();
        var fakeTime = new FakeTimeProvider();
        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
        };

        var provider = new DbConfigConfigurationProvider(options, store, fakeTime, NullLoggerFactory.Instance);
        provider.Load();

        var token = provider.GetReloadToken();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        // Add entry with watermark in the future.
        var t1 = DateTimeOffset.UtcNow.AddSeconds(1);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Key", "v", false, t1, null), TestContext.Current.CancellationToken);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        token.HasChanged.ShouldBeTrue();
    }

    [TimedFact]
    public async Task IOptionsMonitor_ReceivesCallbackWhenReloaded()
    {
        var store = new InMemoryConfigStore();
        var fakeTime = new FakeTimeProvider();
        var reloadInterval = TimeSpan.FromSeconds(5);

        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = reloadInterval,
        };

        var t0 = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "MyOptions:Value", "initial", false, t0, null),
            TestContext.Current.CancellationToken);

        var source = new DbConfigConfigurationSource(options, store, fakeTime, NullLoggerFactory.Instance);

        var configBuilder = new ConfigurationBuilder();
        configBuilder.Add(source);
        var configuration = configBuilder.Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<MyOptions>()
            .Bind(configuration.GetSection("MyOptions"));

        var serviceProvider = services.BuildServiceProvider();
        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<MyOptions>>();

        monitor.CurrentValue.Value.ShouldBe("initial");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.OnChange(_ => tcs.TrySetResult(true));

        var t1 = t0.AddSeconds(1);
        await store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "MyOptions:Value", "updated", false, t1, null),
            TestContext.Current.CancellationToken);

        fakeTime.Advance(reloadInterval);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        monitor.CurrentValue.Value.ShouldBe("updated");
    }

    private sealed class MyOptions
    {
        public string Value { get; set; } = string.Empty;
    }
}
