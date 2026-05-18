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
public sealed class FlatEntriesEndpointTests
{
    private const string Env = "Production";

    [TimedFact]
    public async Task EmptyQuery_ReturnsAllEntries()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Key1", "v1", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppB", Env, string.Empty, "Key2", "v2", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", "Staging", string.Empty, "Key3", "v3", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, "Acme", "Key1", "tenant-v1", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/dbconfig/", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(4);
    }

    [TimedFact]
    public async Task FilterByAppName_ReturnsOnlyMatching()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "K1", "a1", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "K2", "a2", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppB", Env, string.Empty, "K3", "b1", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/dbconfig/?appName=AppA", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(2);
        entries.ShouldAllBe(e => e.GetProperty("appName").GetString() == "AppA");
    }

    [TimedFact]
    public async Task FilterByMultipleParams_AndsThem()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry("AppA", Env, "Acme", "K1", "v1", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, "Globex", "K2", "v2", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppB", Env, "Acme", "K3", "v3", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "K4", "v4", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/?appName=AppA&tenantId=Acme",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(1);
        entries[0].GetProperty("key").GetString().ShouldBe("K1");
    }

    [TimedFact]
    public async Task KeyPrefix_CaseInsensitiveStartsWith()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Stripe:Foo", "v1", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "STRIPE:Bar", "v2", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Other", "v3", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/?keyPrefix=stripe:",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(2);
    }

    [TimedFact]
    public async Task Take_ClampedToMax10000()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        // Seed a single entry so we can verify response, but assert that the cap doesn't
        // throw and that the response succeeds.
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "K1", "v1", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/?take=999999",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();

        // The cap is 10000 — actual result must be ≤ that. With one row seeded, we get 1.
        entries.Length.ShouldBeLessThanOrEqualTo(10000);
        entries.Length.ShouldBe(1);
    }

    [TimedFact]
    public async Task ScopeFilter_BlocksCrossScopeRead()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "K1", "v1", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppB", Env, string.Empty, "K2", "v2", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: "AppA");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/?appName=AppB",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [TimedFact]
    public async Task ScopeFilter_AppliedWhenAppNameOmitted()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "K1", "v1", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppB", Env, string.Empty, "K2", "v2", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: "AppA");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/dbconfig/", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(1);
        entries[0].GetProperty("appName").GetString().ShouldBe("AppA");
    }

    [TimedFact]
    public async Task Ordering_IsDeterministic()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        // Insert in a non-sorted order so any incidental DB ordering would NOT match the
        // expected output. We assert by (AppName, Environment, TenantId, Key) ascending.
        await store.UpsertAsync(new ConfigEntry("AppB", Env, string.Empty, "Key1", "v1", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", "Staging", "Acme", "Key1", "v2", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Key2", "v3", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, "Acme", "Key1", "v4", false, now, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntry("AppA", Env, string.Empty, "Key1", "v5", false, now, null), CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/dbconfig/", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(5);

        // Expected order (Ordinal, ascending):
        // AppA / Production / ""     / Key1
        // AppA / Production / ""     / Key2
        // AppA / Production / Acme   / Key1
        // AppA / Staging    / Acme   / Key1
        // AppB / Production / ""     / Key1
        entries[0].GetProperty("appName").GetString().ShouldBe("AppA");
        entries[0].GetProperty("environment").GetString().ShouldBe(Env);
        entries[0].GetProperty("tenantId").GetString().ShouldBe(string.Empty);
        entries[0].GetProperty("key").GetString().ShouldBe("Key1");

        entries[1].GetProperty("key").GetString().ShouldBe("Key2");
        entries[1].GetProperty("tenantId").GetString().ShouldBe(string.Empty);

        entries[2].GetProperty("tenantId").GetString().ShouldBe("Acme");
        entries[2].GetProperty("environment").GetString().ShouldBe(Env);

        entries[3].GetProperty("environment").GetString().ShouldBe("Staging");

        entries[4].GetProperty("appName").GetString().ShouldBe("AppB");
    }

    [TimedFact]
    public async Task IsSecret_ValuesReturnedDecrypted()
    {
        // Mirrors the existing GET endpoints' behaviour: ciphertext stored at rest,
        // plaintext returned to callers. Use a real encryptor to prove decrypt happens.
        var encryptor = new RoundTripEncryptor();
        var store = new InMemoryConfigStore(encryptor);
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntry("AppA", Env, string.Empty, "Stripe:ApiKey", "sk_test_secret", true, now, null),
            CancellationToken.None);

        await using var app = BuildApp(store, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/dbconfig/", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(1);

        entries[0].GetProperty("isSecret").GetBoolean().ShouldBeTrue();
        entries[0].GetProperty("value").GetString().ShouldBe("sk_test_secret");
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

    /// <summary>
    /// Encryptor that "encrypts" by reversing the string and "decrypts" by reversing again.
    /// Lets the test prove that protect/unprotect actually run round-trip without needing
    /// real Data Protection.
    /// </summary>
    private sealed class RoundTripEncryptor : IConfigEncryptor
    {
        public string Protect(string plaintext) => new([.. plaintext.Reverse()]);

        public string Unprotect(string ciphertext) => new([.. ciphertext.Reverse()]);
    }
}
