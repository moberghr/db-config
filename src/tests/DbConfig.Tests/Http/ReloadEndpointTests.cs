using System.Net;
using System.Net.Http.Json;
using DbConfig.Core;
using DbConfig.Http;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DbConfig.Tests.Http;

[Trait("Category", "Unit")]
public sealed class ReloadEndpointTests
{
    private const string App = "ReloadTestApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task PostReload_Returns204AndCallsTriggerOnSignal()
    {
        var recordingSignal = new RecordingReloadSignal();

        await using var app = BuildApp(new InMemoryConfigStore(), recordingSignal);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.PostAsync(
            "/api/dbconfig/reload",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        recordingSignal.TriggerCallCount.ShouldBe(1);
    }

    [TimedFact]
    public async Task EndToEnd_PutThenReload_IConfigurationReflectsNewValueWithoutAdvancingTime()
    {
        var store = new InMemoryConfigStore();
        var fakeTime = new FakeTimeProvider();
        var reloadInterval = TimeSpan.FromSeconds(30);

        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = reloadInterval,
        };

        // Build the source manually so we can access the reload signal before DI is wired.
        var source = new DbConfigConfigurationSource(options, store, fakeTime, NullLoggerFactory.Instance);
        var configBuilder = new ConfigurationBuilder();
        configBuilder.Add(source);
        var configuration = (IConfiguration)configBuilder.Build();

        // source.Provider is set after Build() — use it as the reload signal.
        var reloadSignal = (IDbConfigReloadSignal)source.Provider!;

        await using var app = BuildApp(store, reloadSignal);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // Arm a TCS on the reload token BEFORE triggering the reload so we don't miss the signal.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IConfigurationRoot)configuration).GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        // PUT a new entry via the HTTP API.
        var body = new { Value = "from-api", IsSecret = false };
        var putResponse = await client.PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/MyKey",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // POST /reload triggers an immediate reload without advancing time.
        var reloadResponse = await client.PostAsync(
            "/api/dbconfig/reload",
            content: null,
            TestContext.Current.CancellationToken);
        reloadResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Wait for the off-thread reload to complete.
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        configuration["MyKey"].ShouldBe("from-api");
    }

    private static WebApplication BuildApp(IConfigStore store, IDbConfigReloadSignal reloadSignal)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(reloadSignal);
        builder.Services.AddSingleton(TimeProvider.System);

        var app = builder.Build();
        app.MapDbConfigHttp("/api/dbconfig");

        return app;
    }

    private sealed class RecordingReloadSignal : IDbConfigReloadSignal
    {
        private int _triggerCallCount;

        public int TriggerCallCount => _triggerCallCount;

        public void Trigger()
        {
            Interlocked.Increment(ref _triggerCallCount);
        }
    }
}
