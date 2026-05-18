using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DbConfig.Core;
using DbConfig.Http;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.Http;

[Trait("Category", "Unit")]
public sealed class EndpointContractTests
{
    private const string App = "ContractTestApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task Put_ThenGet_ReturnsUpsertedEntry()
    {
        await using var app = BuildApp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        const string key = "Section:Sub:Key1";
        var body = new { Value = "hello", IsSecret = false };

        var putResponse = await client.PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await getResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        entry.GetProperty("value").GetString().ShouldBe("hello");
        entry.GetProperty("isSecret").GetBoolean().ShouldBeFalse();
    }

    [TimedFact]
    public async Task Put_WithSecretTrue_Get_ReturnsIsSecretTrueAndUnmaskedValue()
    {
        await using var app = BuildApp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        const string key = "SecretEntry";
        var body = new { Value = "real-secret", IsSecret = true };

        var putResponse = await client.PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await getResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // The API must return isSecret == true (value, not just field presence).
        entry.GetProperty("isSecret").GetBoolean().ShouldBeTrue();

        // The API does NOT mask values; masking is a UI concern only.
        entry.GetProperty("value").GetString().ShouldBe("real-secret");
    }

    [TimedFact]
    public async Task Delete_ThenGet_Returns404()
    {
        await using var app = BuildApp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        const string key = "KeyToDelete";
        var body = new { Value = "temp", IsSecret = false };

        await client.PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            body,
            TestContext.Current.CancellationToken);

        var deleteResponse = await client.DeleteAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TimedFact]
    public async Task FlatQuery_FilteredByApp_ReturnsScopedEntriesOnly()
    {
        await using var app = BuildApp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var store = app.Services.GetRequiredService<IConfigStore>();
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "ListKey1", "v1", false, now, null), TestContext.Current.CancellationToken);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "ListKey2", "v2", false, now, null), TestContext.Current.CancellationToken);
        await store.UpsertAsync(new ConfigEntry("OtherApp", Env, string.Empty, "OtherKey", "other", false, now, null), TestContext.Current.CancellationToken);

        var response = await client.GetAsync(
            $"/api/dbconfig/?appName={App}&environment={Env}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();

        var keys = entries
            .Select(x => x.GetProperty("key").GetString())
            .ToList();

        keys.ShouldContain("ListKey1");
        keys.ShouldContain("ListKey2");
        keys.ShouldNotContain("OtherKey");
    }

    [TimedFact]
    public async Task ColonEncodedKey_AndSlashKey_AddressSameRow()
    {
        await using var app = BuildApp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // PUT using colon-encoded key.
        const string colonKey = "Section%3ASub";
        var body = new { Value = "shared", IsSecret = false };
        var putResponse = await client.PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/{colonKey}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // GET using slash-separated form — should find the same row.
        const string slashKey = "Section/Sub";
        var getResponse = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/{slashKey}",
            TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await getResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        entry.GetProperty("value").GetString().ShouldBe("shared");
    }

    [TimedFact]
    public async Task ResponseJson_UsesCamelCaseFieldNames()
    {
        await using var app = BuildApp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var store = app.Services.GetRequiredService<IConfigStore>();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "CamelKey", "cv", true, now, "tester"), TestContext.Current.CancellationToken);

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/CamelKey",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        // Verify ASP.NET Core default camelCase serialization.
        root.TryGetProperty("appName", out _).ShouldBeTrue();
        root.TryGetProperty("environment", out _).ShouldBeTrue();
        root.TryGetProperty("key", out _).ShouldBeTrue();
        root.TryGetProperty("value", out _).ShouldBeTrue();
        root.TryGetProperty("isSecret", out _).ShouldBeTrue();
        root.TryGetProperty("modifiedUtc", out _).ShouldBeTrue();
        root.TryGetProperty("modifiedBy", out _).ShouldBeTrue();
    }

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore, InMemoryConfigStore>();
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
