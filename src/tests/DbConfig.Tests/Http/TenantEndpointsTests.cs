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
public sealed class TenantEndpointsTests
{
    private const string App = "TenantTestApp";
    private const string Env = "Test";
    private const string TenantAcme = "Acme";
    private const string TenantGlobex = "Globex";

    [TimedFact]
    public async Task Put_WithTenantIdInBody_StoresTenantSpecific()
    {
        var store = new InMemoryConfigStore();
        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        const string key = "Section:Key1";
        var body = new { Value = "acme-value", IsSecret = false, TenantId = TenantAcme };

        var putResponse = await client.PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var tenantEntry = await store.GetForTenantAsync(
            App, Env, TenantAcme, key, TestContext.Current.CancellationToken);
        tenantEntry.ShouldNotBeNull();
        tenantEntry.TenantId.ShouldBe(TenantAcme);
        tenantEntry.Value.ShouldBe("acme-value");

        var globalEntry = await store.GetAsync(App, Env, key, TestContext.Current.CancellationToken);
        globalEntry.ShouldBeNull();
    }

    [TimedFact]
    public async Task Put_WithEmptyTenantIdInBody_StoresGlobalEntry()
    {
        var store = new InMemoryConfigStore();
        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        const string key = "Global:Key1";
        var body = new { Value = "global-value", IsSecret = false, TenantId = string.Empty };

        var putResponse = await client.PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var globalEntry = await store.GetAsync(App, Env, key, TestContext.Current.CancellationToken);
        globalEntry.ShouldNotBeNull();
        globalEntry.TenantId.ShouldBe(string.Empty);
        globalEntry.Value.ShouldBe("global-value");
    }

    [TimedFact]
    public async Task Get_WithTenantId_ReturnsTenantValue()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantAcme, "TenantKey", "acme-val", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "TenantKey", "global-val", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/TenantKey?tenantId={TenantAcme}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        entry.GetProperty("value").GetString().ShouldBe("acme-val");
        entry.GetProperty("tenantId").GetString().ShouldBe(TenantAcme);
    }

    [TimedFact]
    public async Task Get_WithTenantIdAndFallback_FallsBackToGlobalWhenMissing()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        // Only global entry; no Acme-specific entry.
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "FallbackKey", "global-val", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/FallbackKey?tenantId={TenantAcme}&fallback=true",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        entry.GetProperty("value").GetString().ShouldBe("global-val");
        entry.GetProperty("tenantId").GetString().ShouldBe(string.Empty);
    }

    [TimedFact]
    public async Task Get_WithTenantIdAndNoFallback_Returns404WhenMissing()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        // Only global entry; no Acme-specific entry.
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "NoFallbackKey", "global-val", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/NoFallbackKey?tenantId={TenantAcme}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TimedFact]
    public async Task Get_WithoutTenantId_ReturnsGlobalOnly()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey", "global-val", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantAcme, "GlobalKey", "acme-val", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/GlobalKey",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        entry.GetProperty("value").GetString().ShouldBe("global-val");
        entry.GetProperty("tenantId").GetString().ShouldBe(string.Empty);
    }

    [TimedFact]
    public async Task List_WithTenantId_OnlyTenantEntries()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantAcme, "AcmeKey1", "av1", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantAcme, "AcmeKey2", "av2", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantGlobex, "GlobexKey", "gv", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey", "glv", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/?scope={App}&environment={Env}&tenantId={TenantAcme}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(2);

        var keys = entries.Select(x => x.GetProperty("key").GetString()).ToHashSet();
        keys.ShouldContain("AcmeKey1");
        keys.ShouldContain("AcmeKey2");
        keys.ShouldNotContain("GlobexKey");
        keys.ShouldNotContain("GlobalKey");
        entries.All(x => string.Equals(x.GetProperty("tenantId").GetString(), TenantAcme, StringComparison.Ordinal)).ShouldBeTrue();
    }

    [TimedFact]
    public async Task List_WithAllTenants_ReturnsEveryRow()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantAcme, "AcmeKey", "av", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantGlobex, "GlobexKey", "gv", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey", "glv", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // Flat endpoint with no tenantId filter → all tenants (replacement for the old
        // path-based ?allTenants=true behavior).
        var response = await client.GetAsync(
            $"/api/dbconfig/?scope={App}&environment={Env}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(3);

        var keys = entries.Select(x => x.GetProperty("key").GetString()).ToHashSet();
        keys.ShouldContain("AcmeKey");
        keys.ShouldContain("GlobexKey");
        keys.ShouldContain("GlobalKey");
    }

    [TimedFact]
    public async Task List_WithoutTenantId_ReturnsGlobalOnly()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey1", "gv1", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey2", "gv2", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantAcme, "AcmeKey", "av", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        // Flat endpoint with empty tenantId → global-only (replacement for the old
        // path-based default of "global only when no tenantId was passed"). The flat
        // endpoint's "no filter" default returns all tenants, so the explicit empty-string
        // filter is required to reproduce the global-only semantic.
        var response = await client.GetAsync(
            $"/api/dbconfig/?scope={App}&environment={Env}&tenantId=",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(2);

        var keys = entries.Select(x => x.GetProperty("key").GetString()).ToHashSet();
        keys.ShouldContain("GlobalKey1");
        keys.ShouldContain("GlobalKey2");
        keys.ShouldNotContain("AcmeKey");
        entries.All(x => string.Equals(x.GetProperty("tenantId").GetString(), string.Empty, StringComparison.Ordinal)).ShouldBeTrue();
    }

    [TimedFact]
    public async Task Delete_WithTenantId_OnlyAffectsThatTenant()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantAcme, "SharedKey", "acme-val", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "SharedKey", "global-val", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var deleteResponse = await client.DeleteAsync(
            $"/api/dbconfig/{App}/{Env}/SharedKey?tenantId={TenantAcme}",
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var tenantEntry = await store.GetForTenantAsync(
            App, Env, TenantAcme, "SharedKey", TestContext.Current.CancellationToken);
        tenantEntry.ShouldBeNull();

        var globalEntry = await store.GetAsync(App, Env, "SharedKey", TestContext.Current.CancellationToken);
        globalEntry.ShouldNotBeNull();
        globalEntry.Value.ShouldBe("global-val");
    }

    [TimedFact]
    public async Task Audit_WithTenantId_FiltersToTenant()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;
        auditStore.Add(new ConfigAuditEntryRecord(
            Guid.NewGuid(),
            App,
            Env,
            TenantAcme,
            "AuditKey",
            null,
            "acme-val",
            false,
            ConfigAuditAction.Insert,
            now.AddMinutes(-1),
            null));
        auditStore.Add(new ConfigAuditEntryRecord(
            Guid.NewGuid(),
            App,
            Env,
            string.Empty,
            "AuditKey",
            null,
            "global-val",
            false,
            ConfigAuditAction.Insert,
            now,
            null));

        await using var app = BuildAppWithAudit(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/AuditKey?tenantId={TenantAcme}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(1);
        entries[0].GetProperty("tenantId").GetString().ShouldBe(TenantAcme);
    }

    [TimedFact]
    public async Task Audit_WithoutTenantId_ReturnsGlobalOnly()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var now = DateTimeOffset.UtcNow;
        auditStore.Add(new ConfigAuditEntryRecord(
            Guid.NewGuid(),
            App,
            Env,
            TenantAcme,
            "AuditKey2",
            null,
            "acme-val",
            false,
            ConfigAuditAction.Insert,
            now.AddMinutes(-1),
            null));
        auditStore.Add(new ConfigAuditEntryRecord(
            Guid.NewGuid(),
            App,
            Env,
            string.Empty,
            "AuditKey2",
            null,
            "global-val",
            false,
            ConfigAuditAction.Insert,
            now,
            null));

        await using var app = BuildAppWithAudit(auditStore);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/AuditKey2",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        entries.ShouldNotBeNull();
        entries.Length.ShouldBe(1);
        entries[0].GetProperty("tenantId").GetString().ShouldBe(string.Empty);
    }

    [TimedFact]
    public async Task ConfigEntryJson_IncludesTenantIdField()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, TenantAcme, "JsonKey", "jv", false, now, null),
            TestContext.Current.CancellationToken);

        await using var app = BuildApp(store);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/{App}/{Env}/JsonKey?tenantId={TenantAcme}",
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        root.TryGetProperty("tenantId", out var tenantIdProp).ShouldBeTrue();
        tenantIdProp.GetString().ShouldBe(TenantAcme);
    }

    private static WebApplication BuildApp(IConfigStore store)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);

        var app = builder.Build();
        app.MapDbConfigHttp("/api/dbconfig");

        return app;
    }

    private static WebApplication BuildAppWithAudit(InMemoryConfigAuditStore auditStore)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore, InMemoryConfigStore>();
        builder.Services.AddSingleton<IConfigAuditStore>(auditStore);
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
