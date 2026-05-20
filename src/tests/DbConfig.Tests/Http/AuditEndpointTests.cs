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
public sealed class AuditEndpointTests
{
    private const string App = "AuditTestApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task GetAudit_ReturnsHistoryOrderedMostRecentFirst()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        // Add entries out of order to verify the endpoint returns them sorted desc.
        SeedAuditEntry(auditStore, App, Env, "SomeKey", now.AddMinutes(-10), ConfigAuditAction.Insert);
        SeedAuditEntry(auditStore, App, Env, "SomeKey", now.AddMinutes(-5), ConfigAuditAction.Update);
        SeedAuditEntry(auditStore, App, Env, "SomeKey", now, ConfigAuditAction.Update);

        await using var app = BuildApp(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/SomeKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(3);

        // First entry should be the most recent one.
        var first = entries[0].GetProperty("modifiedUtc").GetDateTimeOffset();
        var second = entries[1].GetProperty("modifiedUtc").GetDateTimeOffset();
        var third = entries[2].GetProperty("modifiedUtc").GetDateTimeOffset();

        first.ShouldBeGreaterThan(second);
        second.ShouldBeGreaterThan(third);
    }

    [TimedFact]
    public async Task GetAudit_RespectsTakeParameter()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 10; i++)
        {
            SeedAuditEntry(auditStore, App, Env, "TakeKey", now.AddMinutes(-i), ConfigAuditAction.Update);
        }

        await using var app = BuildApp(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/TakeKey?take=3",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(3);
    }

    [TimedFact]
    public async Task GetAudit_TakeOverMaximum_Returns400()
    {
        var auditStore = new InMemoryConfigAuditStore();

        await using var app = BuildApp(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/AnyKey?take=1000",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotBeNullOrWhiteSpace();
    }

    [TimedFact]
    public async Task GetAudit_TakeNotSpecified_DefaultsTo50()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        // Seed 60 entries; without ?take, the endpoint should return at most 50.
        for (var i = 0; i < 60; i++)
        {
            SeedAuditEntry(auditStore, App, Env, "DefaultTakeKey", now.AddMinutes(-i), ConfigAuditAction.Update);
        }

        await using var app = BuildApp(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/DefaultTakeKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(50);
    }

    [TimedFact]
    public async Task GetAudit_KeyWithNoHistory_ReturnsEmptyArray()
    {
        var auditStore = new InMemoryConfigAuditStore();

        await using var app = BuildApp(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/NoHistoryKey",
            TestContext.Current.CancellationToken);

        // Must return 200 with empty array — not 404.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(0);
    }

    [TimedFact]
    public async Task GetAudit_KeyContainsSlashes_NormalizesToColons()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        // Seed using colon notation (the normalized form stored in the audit store).
        SeedAuditEntry(auditStore, App, Env, "Section:Sub", now, ConfigAuditAction.Insert);

        await using var app = BuildApp(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // Request using slash form in the URL — should be normalized to "Section:Sub".
        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/Section/Sub",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(1);
        entries[0].GetProperty("key").GetString().ShouldBe("Section:Sub");
    }

    [TimedFact]
    public async Task GetAudit_WithScopeFilter_NonMatchingScope_Returns403()
    {
        var auditStore = new InMemoryConfigAuditStore();

        await using var app = BuildApp(auditStore, scopeFilter: "AllowedApp");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/OtherApp/{Env}/audit/SomeKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [TimedFact]
    public async Task GetAudit_WithScopeFilter_MatchingScope_Returns200()
    {
        const string allowedApp = "AllowedApp";
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;
        SeedAuditEntry(auditStore, allowedApp, Env, "SomeKey", now, ConfigAuditAction.Insert);

        await using var app = BuildApp(auditStore, scopeFilter: allowedApp);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{allowedApp}/{Env}/audit/SomeKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task GetAudit_SecretValues_DecryptedInResponse()
    {
        // The audit store is responsible for returning decrypted values on GetHistoryAsync.
        // InMemoryConfigAuditStore stores whatever is passed to Add().
        // We pre-seed with plaintext (simulating what the store returns after decryption).
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;

        // Simulate a secret entry — audit store returns plaintext (decrypted) values.
        var entry = new ConfigAuditEntry(
            Id: Guid.NewGuid(),
            Scope: App,
            Environment: Env,
            TenantId: string.Empty,
            Key: "SecretKey",
            OldValue: null,
            NewValue: "plaintext-secret-value",
            IsSecret: true,
            Action: ConfigAuditAction.Insert,
            ModifiedUtc: now,
            ModifiedBy: "tester");

        auditStore.Add(entry);

        await using var app = BuildApp(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/SecretKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(1);

        entries[0].GetProperty("isSecret").GetBoolean().ShouldBeTrue();
        entries[0].GetProperty("newValue").GetString().ShouldBe("plaintext-secret-value");
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
        string? scopeFilter = null)
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
