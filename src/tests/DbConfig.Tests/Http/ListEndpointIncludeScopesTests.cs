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
public sealed class ListEndpointIncludeScopesTests
{
    private const string App = "MyApp";
    private const string Env = "Test";

    // Regression: no includeScopes → only own scope returned
    [TimedFact]
    public async Task List_WithoutIncludeScopes_ReturnsOnlyOwnScopeEntries()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "OwnKey", "own-value", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "SharedKey", "shared-value", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();

        var keys = entries.Select(e => e.GetProperty("key").GetString()).ToList();
        keys.ShouldContain("OwnKey");
        keys.ShouldNotContain("SharedKey");

        // Must have used the single-scope path (GetAllAsync), not the scoped path.
        store.GetAllAsyncCallCount.ShouldBe(1);
        store.GetAllScopedAsyncCallCount.ShouldBe(0);
    }

    // Rows from all listed scopes returned with correct appName each
    [TimedFact]
    public async Task List_WithIncludeScopes_ReturnsRowsFromAllListedScopes()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "SharedKey", "sv", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("PlatformDefaults", Env, string.Empty, "PlatformKey", "pv", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "OwnKey", "ov", false, now, null), CancellationToken.None);

        // Noise from an unrelated scope — must not appear.
        await store.UpsertAsync(new ConfigEntry("OtherApp", Env, string.Empty, "OtherKey", "other", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}?includeScopes=Shared,PlatformDefaults",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();

        var appNames = entries.Select(e => e.GetProperty("appName").GetString()).ToList();
        appNames.ShouldContain("Shared");
        appNames.ShouldContain("PlatformDefaults");
        appNames.ShouldContain(App);
        appNames.ShouldNotContain("OtherApp");

        // Each row must carry the correct source appName.
        entries.First(e => string.Equals(e.GetProperty("key").GetString(), "SharedKey", StringComparison.Ordinal))
            .GetProperty("appName").GetString().ShouldBe("Shared");
        entries.First(e => string.Equals(e.GetProperty("key").GetString(), "PlatformKey", StringComparison.Ordinal))
            .GetProperty("appName").GetString().ShouldBe("PlatformDefaults");
        entries.First(e => string.Equals(e.GetProperty("key").GetString(), "OwnKey", StringComparison.Ordinal))
            .GetProperty("appName").GetString().ShouldBe(App);
    }

    // Scope order: includeScopes first (in given order), path appName last
    [TimedFact]
    public async Task List_WithIncludeScopes_PreservesScopeOrderInResponse()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntry("PlatformDefaults", Env, string.Empty, "PlatformKey", "pv", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "SharedKey", "sv", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "OwnKey", "ov", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // Query order: PlatformDefaults, Shared — path app (MyApp) must be last.
        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}?includeScopes=PlatformDefaults,Shared",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(3);

        // PlatformDefaults entries come first, Shared second, MyApp last.
        var appNamesInOrder = entries.Select(e => e.GetProperty("appName").GetString()).ToList();
        appNamesInOrder[0].ShouldBe("PlatformDefaults");
        appNamesInOrder[1].ShouldBe("Shared");
        appNamesInOrder[2].ShouldBe(App);
    }

    // Duplicate scope names in the query string are silently collapsed to one
    [TimedFact]
    public async Task List_WithIncludeScopes_DuplicateScopeIgnored()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "SharedKey", "sv", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("PlatformDefaults", Env, string.Empty, "PlatformKey", "pv", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "OwnKey", "ov", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // Shared appears twice in the query string.
        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}?includeScopes=Shared,Shared,PlatformDefaults",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();

        // Shared must appear exactly once (its single key).
        var sharedEntries = entries
            .Where(e => string.Equals(e.GetProperty("appName").GetString(), "Shared", StringComparison.Ordinal))
            .ToList();
        sharedEntries.Count.ShouldBe(1);

        // Total: 1 Shared + 1 PlatformDefaults + 1 MyApp.
        entries.Length.ShouldBe(3);
    }

    // Path appName in the query string is moved to the last position, not doubled
    [TimedFact]
    public async Task List_WithIncludeScopes_PathAppNameInQueryIgnored()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "OwnKey", "ov", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "SharedKey", "sv", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // Path is /MyApp/Test — also passing MyApp in includeScopes.
        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}?includeScopes={App},Shared",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();

        // Own scope must appear exactly once.
        var ownEntries = entries
            .Where(e => string.Equals(e.GetProperty("appName").GetString(), App, StringComparison.Ordinal))
            .ToList();
        ownEntries.Count.ShouldBe(1);

        // Own scope (path app) must be last.
        entries[^1].GetProperty("appName").GetString().ShouldBe(App);

        // Total: 1 Shared + 1 MyApp.
        entries.Length.ShouldBe(2);
    }

    // Whitespace and empty segments in the query value are cleaned up
    [TimedFact]
    public async Task List_WithIncludeScopes_TrimsAndDropsEmptyScopes()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntry("Shared", Env, string.Empty, "SharedKey", "sv", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("PlatformDefaults", Env, string.Empty, "PlatformKey", "pv", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "OwnKey", "ov", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // "Shared, ,,PlatformDefaults" — leading/trailing whitespace and empty segments.
        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}?includeScopes=Shared%2C+%2C%2CPlatformDefaults",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();

        var appNames = entries.Select(e => e.GetProperty("appName").GetString()).Distinct().ToList();
        appNames.ShouldContain("Shared");
        appNames.ShouldContain("PlatformDefaults");
        appNames.ShouldContain(App);

        // Total: 1 Shared + 1 PlatformDefaults + 1 MyApp.
        entries.Length.ShouldBe(3);
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
