using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.E2E;

[Trait("Category", "E2E")]
[Trait("Category", "PostgreSql")]
[Collection(EndToEndPostgreSqlFixture.CollectionName)]
public sealed class FullRoundTripPostgreSqlTests
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;

    public FullRoundTripPostgreSqlTests(EndToEndPostgreSqlFixture fixture)
    {
        _client = fixture.Client;
        _configuration = fixture.Services.GetRequiredService<IConfiguration>();
    }

    [TimedFact(60_000)]
    public async Task Put_ThenPoll_IConfigurationReflectsValue()
    {
        const string key = "PollSection/Sub";
        const string configKey = "PollSection:Sub";

        var body = new { value = "42", isSecret = false };

        var putResponse = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndPostgreSqlFixture.AppName}/{EndToEndPostgreSqlFixture.EnvName}/{key}",
            body,
            TestContext.Current.CancellationToken);

        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Poll for up to 5 seconds (reload interval is 200 ms).
        var reflected = await EndToEndPostgreSqlFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "42", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        reflected.ShouldBeTrue("IConfiguration should reflect the PUT value after polling");
        _configuration[configKey].ShouldBe("42");
    }

    [TimedFact(60_000)]
    public async Task Get_AfterPut_ReturnsUpsertedEntry()
    {
        const string key = "GetSection/Key";

        var body = new { value = "getvalue", isSecret = false };
        var putResponse = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndPostgreSqlFixture.AppName}/{EndToEndPostgreSqlFixture.EnvName}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync(
            $"/api/dbconfig/{EndToEndPostgreSqlFixture.AppName}/{EndToEndPostgreSqlFixture.EnvName}/{key}",
            TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await getResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        entry.GetProperty("value").GetString().ShouldBe("getvalue");
        entry.GetProperty("isSecret").GetBoolean().ShouldBeFalse();
    }
}
