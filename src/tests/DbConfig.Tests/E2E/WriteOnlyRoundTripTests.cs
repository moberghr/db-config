using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.E2E;

/// <summary>
/// A write-only entry PUT over HTTP must reach the host's <see cref="IConfiguration"/> as
/// plaintext while the HTTP read endpoints keep returning a null value.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "SqlServer")]
[Collection(EndToEndFixture.CollectionName)]
public sealed class WriteOnlyRoundTripTests
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;

    public WriteOnlyRoundTripTests(EndToEndFixture fixture)
    {
        _client = fixture.Client;
        _configuration = fixture.Services.GetRequiredService<IConfiguration>();
    }

    [TimedFact(60_000)]
    public async Task Put_WriteOnly_IConfigurationReadsPlaintextAndHttpGetReturnsNull()
    {
        const string key = "WriteOnlySection/ApiKey";
        const string configKey = "WriteOnlySection:ApiKey";
        var url = $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{key}";

        var put = await _client.PutAsJsonAsync(
            url,
            new { value = "e2e-write-only", isSecret = false, isWriteOnly = true },
            TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reflected = await EndToEndFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "e2e-write-only", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        reflected.ShouldBeTrue("IConfiguration should read the write-only plaintext in-process");

        var entry = await _client.GetFromJsonAsync<JsonElement>(url, TestContext.Current.CancellationToken);
        entry.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        entry.GetProperty("isWriteOnly").GetBoolean().ShouldBeTrue();
    }

    [TimedFact(60_000)]
    public async Task Put_WriteOnly_FlatQueryAndAuditNeverContainValue()
    {
        const string key = "WriteOnlySection/FlatKey";
        const string scope = EndToEndFixture.Scope;
        const string env = EndToEndFixture.EnvName;

        var put = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{scope}/{env}/{key}",
            new { value = "e2e-flat-write-only", isSecret = false, isWriteOnly = true },
            TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var entries = await _client.GetStringAsync($"/api/dbconfig/?scope={scope}", TestContext.Current.CancellationToken);
        var audit = await _client.GetStringAsync($"/api/dbconfig/audit?scope={scope}", TestContext.Current.CancellationToken);
        entries.ShouldContain("WriteOnlySection:FlatKey");
        audit.ShouldContain("WriteOnlySection:FlatKey");
        entries.ShouldNotContain("e2e-flat-write-only");
        audit.ShouldNotContain("e2e-flat-write-only");
    }

    [TimedFact(60_000)]
    public async Task Put_UpgradeToWriteOnlyWithSameValue_AuditEndpointsHidePreUpgradeRows()
    {
        const string key = "WriteOnlySection/UpgradeKey";
        const string scope = EndToEndFixture.Scope;
        const string env = EndToEndFixture.EnvName;
        var url = $"/api/dbconfig/{scope}/{env}/{key}";

        var plain = await _client.PutAsJsonAsync(url, new { value = "e2e-same-value", isSecret = true }, TestContext.Current.CancellationToken);
        var upgrade = await _client.PutAsJsonAsync(url, new { value = "e2e-same-value", isSecret = true, isWriteOnly = true }, TestContext.Current.CancellationToken);

        plain.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        upgrade.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var history = await _client.GetStringAsync($"/api/dbconfig/{scope}/{env}/audit/{key}", TestContext.Current.CancellationToken);
        var flat = await _client.GetStringAsync($"/api/dbconfig/audit?scope={scope}&keyPrefix=WriteOnlySection:UpgradeKey", TestContext.Current.CancellationToken);
        history.ShouldContain("WriteOnlySection:UpgradeKey");
        flat.ShouldContain("WriteOnlySection:UpgradeKey");
        history.ShouldNotContain("e2e-same-value");
        flat.ShouldNotContain("e2e-same-value");
    }
}
