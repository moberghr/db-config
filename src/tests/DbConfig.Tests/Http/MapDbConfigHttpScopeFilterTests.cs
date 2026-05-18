using System.Net;
using System.Net.Http.Json;
using DbConfig.Core;
using DbConfig.Http;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.Http;

[Trait("Category", "Unit")]
public sealed class MapDbConfigHttpScopeFilterTests
{
    private const string Env = "Prod";

    // Regression: without scopeFilter, all app names are accessible on single-key GETs.
    [TimedFact]
    public async Task ScopeFilter_Null_AllAppNamesAllowed()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Key1", "v1", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppB", Env, string.Empty, "Key2", "v2", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var responseA = await client.GetAsync(
            $"/api/dbconfig/AppA/{Env}/Key1",
            TestContext.Current.CancellationToken);
        responseA.StatusCode.ShouldBe(HttpStatusCode.OK);

        var responseB = await client.GetAsync(
            $"/api/dbconfig/AppB/{Env}/Key2",
            TestContext.Current.CancellationToken);
        responseB.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // scopeFilter="MyApp" — matching path appName is allowed on single-key GETs.
    [TimedFact]
    public async Task ScopeFilter_MatchingAppName_GetEntry_Allowed()
    {
        const string myApp = "MyApp";
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(myApp, Env, string.Empty, "Key1", "v1", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: myApp);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{myApp}/{Env}/Key1",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // scopeFilter="MyApp" — non-matching appName on single-key GET returns 403.
    [TimedFact]
    public async Task ScopeFilter_NonMatchingAppName_GetEntry_Returns403()
    {
        var store = new InMemoryConfigStore();

        await using var app = BuildApp(store, scopeFilter: "MyApp");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/OtherApp/{Env}/SomeKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // scopeFilter="MyApp" — non-matching appName on PUT returns 403.
    [TimedFact]
    public async Task ScopeFilter_NonMatchingAppName_PutEntry_Returns403()
    {
        var store = new InMemoryConfigStore();

        await using var app = BuildApp(store, scopeFilter: "MyApp");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var body = new { Value = "v", IsSecret = false };
        var response = await client.PutAsJsonAsync(
            $"/api/dbconfig/OtherApp/{Env}/SomeKey",
            body,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // scopeFilter="MyApp" — non-matching appName on DELETE returns 403.
    [TimedFact]
    public async Task ScopeFilter_NonMatchingAppName_DeleteEntry_Returns403()
    {
        var store = new InMemoryConfigStore();

        await using var app = BuildApp(store, scopeFilter: "MyApp");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.DeleteAsync(
            $"/api/dbconfig/OtherApp/{Env}/SomeKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // scopeFilter="MyApp" — POST /reload has no appName route value and is always allowed.
    [TimedFact]
    public async Task ScopeFilter_ReloadEndpoint_AlwaysAllowed()
    {
        var store = new InMemoryConfigStore();

        await using var app = BuildApp(store, scopeFilter: "MyApp");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.PostAsync(
            "/api/dbconfig/reload",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static WebApplication BuildApp(InMemoryConfigStore store, string? scopeFilter)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore>(store);
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);

        var app = builder.Build();
        app.MapDbConfigHttp("/api/dbconfig", scopeFilter: scopeFilter);

        return app;
    }

    private sealed class NoOpReloadSignal : IDbConfigReloadSignal
    {
        public void Trigger()
        {
        }
    }
}
