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
public sealed class QueryAuditEndpointTests
{
    private const string Env = "Production";

    [TimedFact]
    public async Task EmptyQuery_ReturnsAllAuditEntries()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        SeedAuditEntry(auditStore, "AppA", Env, "Key1", now.AddMinutes(-5), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppA", Env, "Key1", now.AddMinutes(-4), ConfigAuditAction.Update);
        SeedAuditEntry(auditStore, "AppA", Env, "Key2", now.AddMinutes(-3), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppB", Env, "Key3", now.AddMinutes(-2), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppB", Env, "Key3", now.AddMinutes(-1), ConfigAuditAction.Delete);

        await using var app = BuildApp(auditStore, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/dbconfig/audit", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(5);
    }

    [TimedFact]
    public async Task FilterByScope_ReturnsOnlyMatching()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        SeedAuditEntry(auditStore, "AppA", Env, "Key1", now.AddMinutes(-3), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppA", Env, "Key2", now.AddMinutes(-2), ConfigAuditAction.Update);
        SeedAuditEntry(auditStore, "AppB", Env, "Key3", now.AddMinutes(-1), ConfigAuditAction.Insert);

        await using var app = BuildApp(auditStore, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/audit?scope=AppA",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(2);
        entries.ShouldAllBe(e => e.GetProperty("scope").GetString() == "AppA");
    }

    [TimedFact]
    public async Task FilterByAction_ReturnsOnlyMatching()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        SeedAuditEntry(auditStore, "AppA", Env, "Key1", now.AddMinutes(-4), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppA", Env, "Key1", now.AddMinutes(-3), ConfigAuditAction.Update);
        SeedAuditEntry(auditStore, "AppA", Env, "Key2", now.AddMinutes(-2), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppA", Env, "Key2", now.AddMinutes(-1), ConfigAuditAction.Delete);

        await using var app = BuildApp(auditStore, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/audit?action=Insert",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(2);
        entries.ShouldAllBe(e => e.GetProperty("action").GetString() == "Insert");
    }

    [TimedFact]
    public async Task KeyPrefix_CaseInsensitive()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        SeedAuditEntry(auditStore, "AppA", Env, "Stripe:Foo", now.AddMinutes(-3), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppA", Env, "STRIPE:Bar", now.AddMinutes(-2), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppA", Env, "Other:Key", now.AddMinutes(-1), ConfigAuditAction.Insert);

        await using var app = BuildApp(auditStore, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/audit?keyPrefix=stripe",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(2);
    }

    [TimedFact]
    public async Task OrderByModifiedDesc()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        // Insert out of order — the endpoint must sort descending.
        SeedAuditEntry(auditStore, "AppA", Env, "K1", now.AddMinutes(-10), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "AppA", Env, "K1", now.AddMinutes(-2), ConfigAuditAction.Update);
        SeedAuditEntry(auditStore, "AppA", Env, "K1", now.AddMinutes(-6), ConfigAuditAction.Update);

        await using var app = BuildApp(auditStore, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/dbconfig/audit", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(3);

        var t0 = entries[0].GetProperty("modifiedUtc").GetDateTimeOffset();
        var t1 = entries[1].GetProperty("modifiedUtc").GetDateTimeOffset();
        var t2 = entries[2].GetProperty("modifiedUtc").GetDateTimeOffset();

        t0.ShouldBeGreaterThan(t1);
        t1.ShouldBeGreaterThan(t2);
    }

    [TimedFact]
    public async Task Take_ClampedToMax()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;
        SeedAuditEntry(auditStore, "AppA", Env, "K1", now, ConfigAuditAction.Insert);

        await using var app = BuildApp(auditStore, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/audit?take=999999",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBeLessThanOrEqualTo(10000);
        entries.Length.ShouldBe(1);
    }

    [TimedFact]
    public async Task ScopeFilter_BlocksCrossScopeRead()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;
        SeedAuditEntry(auditStore, "A", Env, "K1", now, ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "B", Env, "K2", now, ConfigAuditAction.Insert);

        await using var app = BuildApp(auditStore, scopeFilter: "A");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/audit?scope=B",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [TimedFact]
    public async Task ScopeFilter_ForcesOwnScope()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;
        SeedAuditEntry(auditStore, "A", Env, "K1", now.AddMinutes(-2), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, "B", Env, "K2", now.AddMinutes(-1), ConfigAuditAction.Insert);

        await using var app = BuildApp(auditStore, scopeFilter: "A");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/dbconfig/audit", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(1);
        entries[0].GetProperty("scope").GetString().ShouldBe("A");
    }

    [TimedFact]
    public async Task InvalidAction_Returns400()
    {
        var auditStore = new InMemoryConfigAuditStore();

        await using var app = BuildApp(auditStore, scopeFilter: null);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/dbconfig/audit?action=Bogus",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("Bogus");
    }

    private static void SeedAuditEntry(
        InMemoryConfigAuditStore auditStore,
        string scope,
        string environment,
        string key,
        DateTimeOffset modifiedUtc,
        ConfigAuditAction action)
    {
        var entry = new ConfigAuditEntry(
            Id: Guid.NewGuid(),
            Scope: scope,
            Environment: environment,
            TenantId: string.Empty,
            Key: key,
            OldValue: action == ConfigAuditAction.Insert ? null : "old-value",
            NewValue: action == ConfigAuditAction.Delete ? null : "new-value",
            IsSecret: false,
            Action: action,
            ModifiedUtc: modifiedUtc,
            ModifiedBy: null);

        auditStore.Add(entry);
    }

    private static WebApplication BuildApp(
        InMemoryConfigAuditStore auditStore,
        string? scopeFilter)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore, InMemoryConfigStore>();
        builder.Services.AddSingleton<IConfigAuditStore>(auditStore);
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
