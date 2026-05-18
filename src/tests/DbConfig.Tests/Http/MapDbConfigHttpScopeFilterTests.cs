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

    // Regression: without scopeFilter, all app names are accessible.
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
            $"/api/dbconfig/AppA/{Env}",
            TestContext.Current.CancellationToken);
        responseA.StatusCode.ShouldBe(HttpStatusCode.OK);

        var responseB = await client.GetAsync(
            $"/api/dbconfig/AppB/{Env}",
            TestContext.Current.CancellationToken);
        responseB.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // scopeFilter="MyApp" — matching path appName is allowed (GET list).
    [TimedFact]
    public async Task ScopeFilter_MatchingAppName_GetEntries_Allowed()
    {
        const string myApp = "MyApp";
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(myApp, Env, string.Empty, "Key1", "v1", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: myApp);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{myApp}/{Env}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // scopeFilter="MyApp" — non-matching appName on GET list returns 403.
    [TimedFact]
    public async Task ScopeFilter_NonMatchingAppName_GetEntries_Returns403()
    {
        var store = new InMemoryConfigStore();

        await using var app = BuildApp(store, scopeFilter: "MyApp");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/OtherApp/{Env}",
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

    // scopeFilter="MyApp" — GET single entry with mismatched path appName returns 403.
    [TimedFact]
    public async Task ScopeFilter_GetSingleEntry_PathScopeMismatch_Returns403()
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

    // scopeFilter="MyApp" — GET with ?includeScopes=Shared uses path appName "MyApp" which
    // matches the filter. The query parameter adds read scopes but does NOT affect the filter.
    [TimedFact]
    public async Task ScopeFilter_WithIncludeScopesQuery_OnlyFiltersPathAppName()
    {
        const string myApp = "MyApp";
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(myApp, Env, string.Empty, "OwnKey", "ov", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "SharedKey", "sv", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: myApp);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // Path appName is "MyApp" (matches filter). includeScopes adds "Shared" as a read scope.
        var response = await client.GetAsync(
            $"/api/dbconfig/{myApp}/{Env}?includeScopes=Shared",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
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
