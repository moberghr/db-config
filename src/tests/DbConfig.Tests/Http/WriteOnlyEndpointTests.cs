using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DbConfig.Core;
using DbConfig.Http;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace DbConfig.Tests.Http;

/// <summary>
/// Verifies that every HTTP endpoint which serializes values redacts write-only entries and
/// write-only audit rows, while plain <c>IsSecret</c> entries keep returning plaintext.
/// </summary>
[Trait("Category", "Unit")]
public sealed class WriteOnlyEndpointTests
{
    private const string App = "WriteOnlyApp";
    private const string Env = "Test";
    private const string WriteOnlyValue = "write-only-plaintext";
    private const string SecretValue = "revealable-secret";

    [TimedFact]
    public async Task GetEntry_WriteOnlyEntry_ReturnsNullValueAndFlag()
    {
        var (store, _) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store);

        var response = await app.GetTestClient().GetAsync(
            $"/api/dbconfig/{App}/{Env}/Api/Key",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        entry.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        entry.GetProperty("isWriteOnly").GetBoolean().ShouldBeTrue();
        entry.GetProperty("isSecret").GetBoolean().ShouldBeTrue();
    }

    [TimedFact]
    public async Task GetEntry_PlainSecretEntry_ReturnsPlaintext()
    {
        var (store, _) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store);

        var response = await app.GetTestClient().GetAsync(
            $"/api/dbconfig/{App}/{Env}/Db/Password",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        entry.GetProperty("value").GetString().ShouldBe(SecretValue);
        entry.GetProperty("isWriteOnly").GetBoolean().ShouldBeFalse();
    }

    [TimedFact]
    public async Task QueryEntries_MixedEntries_RedactsOnlyWriteOnly()
    {
        var (store, _) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store);

        var response = await app.GetTestClient().GetAsync(
            $"/api/dbconfig/?scope={App}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain(WriteOnlyValue);
        var entries = JsonSerializer.Deserialize<JsonElement[]>(body)!;
        entries.Single(x => string.Equals(x.GetProperty("key").GetString(), "Api:Key", StringComparison.Ordinal))
            .GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        entries.Single(x => string.Equals(x.GetProperty("key").GetString(), "Db:Password", StringComparison.Ordinal))
            .GetProperty("value").GetString().ShouldBe(SecretValue);
    }

    [TimedFact]
    public async Task GetAuditHistory_WriteOnlyEntry_ReturnsNullOldAndNewValues()
    {
        var (store, auditStore) = CreateStores();
        await SeedAsync(store);
        await store.UpsertAsync(WriteOnly("Api:Key", "rotated-plaintext"), CancellationToken.None);
        await using var app = await StartAsync(store, auditStore);

        var response = await app.GetTestClient().GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/Api/Key",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        rows.ShouldNotBeNull();
        rows.Length.ShouldBe(2);
        rows.ShouldAllBe(x => x.GetProperty("isWriteOnly").GetBoolean());
        rows.ShouldAllBe(x => x.GetProperty("oldValue").ValueKind == JsonValueKind.Null);
        rows.ShouldAllBe(x => x.GetProperty("newValue").ValueKind == JsonValueKind.Null);
    }

    [TimedFact]
    public async Task QueryAudit_MixedRows_RedactsOnlyWriteOnly()
    {
        var (store, auditStore) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store, auditStore);

        var response = await app.GetTestClient().GetAsync(
            $"/api/dbconfig/audit?scope={App}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain(WriteOnlyValue);
        var rows = JsonSerializer.Deserialize<JsonElement[]>(body)!;
        rows.Single(x => string.Equals(x.GetProperty("key").GetString(), "Db:Password", StringComparison.Ordinal))
            .GetProperty("newValue").GetString().ShouldBe(SecretValue);
        var writeOnlyRow = rows.Single(x => string.Equals(x.GetProperty("key").GetString(), "Api:Key", StringComparison.Ordinal));
        writeOnlyRow.GetProperty("isWriteOnly").GetBoolean().ShouldBeTrue();
        writeOnlyRow.GetProperty("newValue").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [TimedFact]
    public async Task PutEntry_WriteOnlyFlag_StoresFlagAndPlaintextInProcess()
    {
        var (store, _) = CreateStores();
        await using var app = await StartAsync(store);

        var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/New/Key",
            new { value = "fresh-value", isSecret = false, isWriteOnly = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await store.GetAsync(App, Env, "New:Key", CancellationToken.None);
        stored.ShouldNotBeNull();
        stored!.IsWriteOnly.ShouldBeTrue();
        stored.IsSecret.ShouldBeTrue();
        stored.Value.ShouldBe("fresh-value");
    }

    [TimedFact]
    public async Task Put_DowngradeWriteOnly_ReplacesValueWithoutRevealingOld()
    {
        var (store, auditStore) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store, auditStore);
        var client = app.GetTestClient();

        var put = await client.PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/Api/Key",
            new { value = "now-readable", isSecret = false, isWriteOnly = false },
            TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var entryBody = await client.GetStringAsync($"/api/dbconfig/{App}/{Env}/Api/Key", TestContext.Current.CancellationToken);
        var auditBody = await client.GetStringAsync($"/api/dbconfig/{App}/{Env}/audit/Api/Key", TestContext.Current.CancellationToken);
        entryBody.ShouldContain("now-readable");
        entryBody.ShouldNotContain(WriteOnlyValue);
        auditBody.ShouldNotContain(WriteOnlyValue);
    }

    [TimedFact]
    public async Task GetAuditHistory_UpgradedToWriteOnlyWithSameValue_HidesPreUpgradeRows()
    {
        var (store, auditStore) = CreateStores();
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Stripe:ApiKey", "sk_live_unchanged", true, DateTimeOffset.UtcNow, "tester"),
            CancellationToken.None);
        await store.UpsertAsync(WriteOnly("Stripe:ApiKey", "sk_live_unchanged"), CancellationToken.None);
        await using var app = await StartAsync(store, auditStore);

        var response = await app.GetTestClient().GetAsync(
            $"/api/dbconfig/{App}/{Env}/audit/Stripe/ApiKey",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("sk_live_unchanged");
        var rows = JsonSerializer.Deserialize<JsonElement[]>(body)!;
        rows.Length.ShouldBe(2);
        rows.ShouldAllBe(x => x.GetProperty("isWriteOnly").GetBoolean());
    }

    [TimedFact]
    public async Task QueryAudit_UpgradedToWriteOnlyWithSameValue_HidesPreUpgradeRows()
    {
        var (store, auditStore) = CreateStores();
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Stripe:ApiKey", "sk_live_unchanged", true, DateTimeOffset.UtcNow, "tester"),
            CancellationToken.None);
        await store.UpsertAsync(WriteOnly("Stripe:ApiKey", "sk_live_unchanged"), CancellationToken.None);
        await using var app = await StartAsync(store, auditStore);

        var response = await app.GetTestClient().GetAsync(
            $"/api/dbconfig/audit?scope={App}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("sk_live_unchanged");
    }

    [TimedFact]
    public async Task PutEntry_OmittedWriteOnlyFlag_KeepsExistingFlag()
    {
        var (store, _) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store);

        var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/Api/Key",
            new { value = "rotated-by-old-script", isSecret = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await store.GetAsync(App, Env, "Api:Key", CancellationToken.None);
        stored.ShouldNotBeNull();
        stored!.IsWriteOnly.ShouldBeTrue();
        stored.Value.ShouldBe("rotated-by-old-script");
    }

    [TimedFact]
    public async Task GetAuditHistory_TenantUpgradedToWriteOnlyWithSameValue_HidesPreUpgradeRows()
    {
        var (store, auditStore) = CreateStores();
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Acme", "Stripe:ApiKey", "sk_acme_unchanged", true, DateTimeOffset.UtcNow, "tester"),
            CancellationToken.None);
        await store.UpsertAsync(WriteOnly("Stripe:ApiKey", "sk_acme_unchanged") with { TenantId = "Acme" }, CancellationToken.None);
        await using var app = await StartAsync(store, auditStore);

        var body = await app.GetTestClient().GetStringAsync(
            $"/api/dbconfig/{App}/{Env}/audit/Stripe/ApiKey?tenantId=Acme",
            TestContext.Current.CancellationToken);

        body.ShouldNotContain("sk_acme_unchanged");
        var rows = JsonSerializer.Deserialize<JsonElement[]>(body)!;
        rows.Length.ShouldBe(2);
        rows.ShouldAllBe(x => x.GetProperty("oldValue").ValueKind == JsonValueKind.Null);
    }

    [TimedFact]
    public async Task GetAuditHistory_PlainSecretKey_ReturnsPlaintext()
    {
        var (store, auditStore) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store, auditStore);

        var rows = await app.GetTestClient().GetFromJsonAsync<JsonElement[]>(
            $"/api/dbconfig/{App}/{Env}/audit/Db/Password",
            TestContext.Current.CancellationToken);

        var row = rows.ShouldHaveSingleItem();
        row.GetProperty("newValue").GetString().ShouldBe(SecretValue);
        row.GetProperty("isWriteOnly").GetBoolean().ShouldBeFalse();
    }

    [TimedFact]
    public async Task QueryAudit_DeletedWriteOnlyEntry_KeepsValuesHidden()
    {
        var (store, auditStore) = CreateStores();
        await SeedAsync(store);
        await store.UpsertAsync(WriteOnly("Api:Key", "rotated-plaintext"), CancellationToken.None);
        await store.DeleteAsync(App, Env, "Api:Key", CancellationToken.None);
        await using var app = await StartAsync(store, auditStore);

        var body = await app.GetTestClient().GetStringAsync(
            $"/api/dbconfig/audit?scope={App}&keyPrefix=Api",
            TestContext.Current.CancellationToken);

        body.ShouldNotContain(WriteOnlyValue);
        body.ShouldNotContain("rotated-plaintext");
        var rows = JsonSerializer.Deserialize<JsonElement[]>(body)!;
        rows.Length.ShouldBe(3);
        rows.ShouldAllBe(x => x.GetProperty("isWriteOnly").GetBoolean());
    }

    [TimedFact]
    public async Task PutEntry_TenantOmittedWriteOnlyFlag_KeepsTenantFlag()
    {
        var (store, _) = CreateStores();
        await store.UpsertAsync(WriteOnly("Api:Key", "acme-secret") with { TenantId = "Acme" }, CancellationToken.None);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Api:Key", "global-plain", false, DateTimeOffset.UtcNow, "tester"),
            CancellationToken.None);
        await using var app = await StartAsync(store);

        var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/Api/Key",
            new { value = "acme-rotated", isSecret = true, tenantId = "Acme" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var tenant = await store.GetForTenantAsync(App, Env, "Acme", "Api:Key", CancellationToken.None);
        tenant.ShouldNotBeNull();
        tenant!.IsWriteOnly.ShouldBeTrue();
        tenant.Value.ShouldBe("acme-rotated");
    }

    [TimedFact]
    public async Task PutEntry_OmittedWriteOnlyFlagOnPlainEntry_StaysReadable()
    {
        var (store, _) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store);

        var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/Db/Password",
            new { value = "new-password", isSecret = true, isWriteOnly = (bool?)null },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await store.GetAsync(App, Env, "Db:Password", CancellationToken.None);
        stored.ShouldNotBeNull();
        stored!.IsWriteOnly.ShouldBeFalse();
        stored.Value.ShouldBe("new-password");
    }

    [TimedFact]
    public async Task PutEntry_NullValueOverWriteOnly_Returns400AndKeepsValue()
    {
        var (store, _) = CreateStores();
        await SeedAsync(store);
        await using var app = await StartAsync(store);

        var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/Api/Key",
            new { value = (string?)null, isSecret = true, isWriteOnly = false },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var stored = await store.GetAsync(App, Env, "Api:Key", CancellationToken.None);
        stored.ShouldNotBeNull();
        stored!.Value.ShouldBe(WriteOnlyValue);
        stored.IsWriteOnly.ShouldBeTrue();
    }

    [TimedFact]
    public async Task PutEntry_StoreDropsWriteOnlyFlag_Returns500()
    {
        var store = new Mock<IConfigStore>();
        store
            .Setup(x => x.GetAsync(App, Env, "New:Key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigEntryRecord(App, Env, string.Empty, "New:Key", "fresh", true, DateTimeOffset.UtcNow, "tester"));
        await using var app = await StartAsync(store.Object);

        var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/dbconfig/{App}/{Env}/New/Key",
            new { value = "fresh", isSecret = true, isWriteOnly = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    private static (InMemoryConfigStore Store, InMemoryConfigAuditStore AuditStore) CreateStores()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(encryptor: null, auditStore, enableAuditLog: true);

        return (store, auditStore);
    }

    private static async Task SeedAsync(InMemoryConfigStore store)
    {
        await store.UpsertAsync(WriteOnly("Api:Key", WriteOnlyValue), CancellationToken.None);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Db:Password", SecretValue, true, DateTimeOffset.UtcNow, "tester"),
            CancellationToken.None);
    }

    private static ConfigEntryRecord WriteOnly(string key, string value)
    {
        return new ConfigEntryRecord(App, Env, string.Empty, key, value, false, DateTimeOffset.UtcNow, "tester", IsWriteOnly: true);
    }

    private static async Task<WebApplication> StartAsync(IConfigStore store, InMemoryConfigAuditStore? auditStore = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore>(store);
        if (auditStore is not null)
        {
            builder.Services.AddSingleton<IConfigAuditStore>(auditStore);
        }

        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);

        var app = builder.Build();
        app.MapDbConfigHttp("/api/dbconfig");
        await app.StartAsync(TestContext.Current.CancellationToken);

        return app;
    }

    private sealed class NoOpReloadSignal : IDbConfigReloadSignal
    {
        public void Trigger()
        {
        }
    }
}
