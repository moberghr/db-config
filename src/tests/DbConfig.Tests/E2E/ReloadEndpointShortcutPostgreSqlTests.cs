using System.Net;
using System.Net.Http.Json;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.E2E;

[Trait("Category", "E2E")]
[Trait("Category", "PostgreSql")]
[Collection(EndToEndPostgreSqlFixture.CollectionName)]
public sealed class ReloadEndpointShortcutPostgreSqlTests
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;

    public ReloadEndpointShortcutPostgreSqlTests(EndToEndPostgreSqlFixture fixture)
    {
        _client = fixture.Client;
        _configuration = fixture.Services.GetRequiredService<IConfiguration>();
    }

    [TimedFact(60_000)]
    public async Task Put_ThenExplicitReload_ImmediateReflection()
    {
        const string key = "ReloadShortcut/Key";
        const string configKey = "ReloadShortcut:Key";

        var body = new { value = "immediate", isSecret = false };
        var putResponse = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{EndToEndPostgreSqlFixture.Scope}/{EndToEndPostgreSqlFixture.EnvName}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // POST /reload triggers an immediate out-of-band reload — bypasses the 200 ms poll interval.
        var reloadResponse = await _client.PostAsync(
            "/api/dbconfig/reload",
            content: null,
            TestContext.Current.CancellationToken);
        reloadResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Expect reflection within 2 s — the meaningful assertion is that reflection happens
        // without advancing FakeTime past the 200 ms poll interval, not a sub-100 ms deadline.
        var reflected = await EndToEndPostgreSqlFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "immediate", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));

        reflected.ShouldBeTrue(
            "IConfiguration should reflect the PUT value immediately after POST /reload, " +
            "without waiting for the 200 ms polling interval");

        _configuration[configKey].ShouldBe("immediate");
    }
}
