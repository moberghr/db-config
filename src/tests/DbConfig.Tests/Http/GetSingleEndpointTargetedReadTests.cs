using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DbConfig.Core;
using DbConfig.Http;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.Http;

[Trait("Category", "Unit")]
public sealed class GetSingleEndpointTargetedReadTests
{
    private const string App = "TargetedReadApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task GetSingle_ReturnsExpectedEntry()
    {
        var store = new InMemoryConfigStore();
        await SeedEntriesAsync(store);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/Key5",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        entry.GetProperty("key").GetString().ShouldBe("Key5");
        entry.GetProperty("value").GetString().ShouldBe("Value5");
    }

    [TimedFact]
    public async Task GetSingle_UsesTargetedGetAsync_NotGetAllAsync()
    {
        var store = new InMemoryConfigStore();
        await SeedEntriesAsync(store);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/Key3",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // GetAsync must have been called exactly once.
        store.GetAsyncCallCount.ShouldBe(1);

        // GetAllAsync must NOT have been called at all — the endpoint must be on the targeted path.
        store.GetAllAsyncCallCount.ShouldBe(0);
    }

    [TimedFact]
    public async Task GetSingle_NotFound_Returns404_AndUsesTargetedPath()
    {
        var store = new InMemoryConfigStore();
        await SeedEntriesAsync(store);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/NonExistentKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Even for a miss, we must go through GetAsync — not GetAllAsync.
        store.GetAsyncCallCount.ShouldBe(1);
        store.GetAllAsyncCallCount.ShouldBe(0);
    }

    private static async Task SeedEntriesAsync(InMemoryConfigStore store)
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 10; i++)
        {
            await store.UpsertAsync(
                new ConfigEntry(App, Env, string.Empty, $"Key{i}", $"Value{i}", false, now, null),
                CancellationToken.None);
        }
    }

    private static WebApplication BuildApp(InMemoryConfigStore store)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore>(store);
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);

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
