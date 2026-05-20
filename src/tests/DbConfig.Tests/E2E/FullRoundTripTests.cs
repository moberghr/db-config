using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.E2E;

[Trait("Category", "E2E")]
[Trait("Category", "SqlServer")]
[Collection(EndToEndFixture.CollectionName)]
public sealed class FullRoundTripTests
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;

    public FullRoundTripTests(EndToEndFixture fixture)
    {
        _client = fixture.Client;
        _configuration = fixture.Services.GetRequiredService<IConfiguration>();
    }

    [TimedFact(60_000)]
    public async Task Put_ThenPoll_IConfigurationReflectsValue()
    {
        // Use a unique key per test to avoid cross-test interference.
        const string key = "PollSection/Sub";
        const string configKey = "PollSection:Sub";

        var body = new { value = "42", isSecret = false };

        var putResponse = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{key}",
            body,
            TestContext.Current.CancellationToken);

        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Poll for up to 5 seconds (reload interval is 200 ms).
        var reflected = await EndToEndFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "42", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        reflected.ShouldBeTrue("IConfiguration should reflect the PUT value after polling");
        _configuration[configKey].ShouldBe("42");
    }

    [TimedFact(60_000)]
    public async Task Put_OverwriteExisting_IConfigurationReflectsLatest()
    {
        const string key = "OverwriteSection/Key";
        const string configKey = "OverwriteSection:Key";

        // First PUT.
        var body1 = new { value = "first", isSecret = false };
        var put1 = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{key}",
            body1,
            TestContext.Current.CancellationToken);
        put1.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Wait for first value to reflect.
        await EndToEndFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "first", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Second PUT overwrites.
        var body2 = new { value = "latest", isSecret = false };
        var put2 = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{key}",
            body2,
            TestContext.Current.CancellationToken);
        put2.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Poll for the latest value.
        var reflected = await EndToEndFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "latest", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        reflected.ShouldBeTrue("IConfiguration should reflect the latest PUT value after polling");
        _configuration[configKey].ShouldBe("latest");
    }

    [TimedFact(60_000)]
    public async Task Delete_ThenPoll_IConfigurationLosesKey()
    {
        const string key = "DeleteSection/Key";
        const string configKey = "DeleteSection:Key";

        // PUT the entry first.
        var body = new { value = "to-delete", isSecret = false };
        var putResponse = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Wait for it to appear in IConfiguration.
        var appeared = await EndToEndFixture.WaitUntilAsync(
            () => _configuration[configKey] is not null,
            TimeSpan.FromSeconds(5));
        appeared.ShouldBeTrue("key should appear in IConfiguration before deletion");

        // DELETE the entry.
        var deleteResponse = await _client.DeleteAsync(
            $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{key}",
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Poll for the key to disappear. After a delete alone the watermark does not advance,
        // so we PUT a sentinel entry to advance the watermark and trigger a reload.
        const string sentinelKey = "DeleteSection/Sentinel";
        var sentinelBody = new { value = "sentinel", isSecret = false };
        await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{sentinelKey}",
            sentinelBody,
            TestContext.Current.CancellationToken);

        var disappeared = await EndToEndFixture.WaitUntilAsync(
            () => _configuration[configKey] is null,
            TimeSpan.FromSeconds(5));

        disappeared.ShouldBeTrue("IConfiguration should no longer contain the deleted key after polling");
        _configuration[configKey].ShouldBeNull();
    }

    [TimedFact(60_000)]
    public async Task Get_AfterPut_ReturnsUpsertedEntry()
    {
        const string key = "GetSection/Key";

        var body = new { value = "getvalue", isSecret = false };
        var putResponse = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync(
            $"/api/dbconfig/{EndToEndFixture.Scope}/{EndToEndFixture.EnvName}/{key}",
            TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await getResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        entry.GetProperty("value").GetString().ShouldBe("getvalue");
        entry.GetProperty("isSecret").GetBoolean().ShouldBeFalse();
    }
}
