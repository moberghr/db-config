using System.Net;
using System.Net.Http.Json;
using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Http;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Testcontainers.MsSql;

namespace DbConfig.Tests.E2E;

[CollectionDefinition("E2E_IncludeScopes")]
public sealed class E2EIncludeScopesCollection : ICollectionFixture<IncludeScopesE2EFixture>;

/// <summary>
/// Dedicated fixture for IncludeScopes E2E tests.
/// Starts its own SQL Server container and builds a host configured with
/// Scope = "PaymentService", Environment = "Test", IncludeScopes = ["Shared"].
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "SqlServer")]
public sealed class IncludeScopesE2EFixture : IAsyncLifetime
{
    public const string CollectionName = "E2E_IncludeScopes";
    public const string Scope = "PaymentService";
    public const string SharedScope = "Shared";
    public const string EnvName = "Test";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _app!.Services;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await _container.StartAsync(ct);

        var connectionString = _container.GetConnectionString();

        // Apply EF migrations before starting the host.
        var migrateOptions = new DbContextOptionsBuilder<DbConfigDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer"))
            .Options;

        await using (var ctx = new DbConfigDbContext(migrateOptions))
        {
            await ctx.Database.MigrateAsync(ct);
        }

        // Build the WebApplication with IncludeScopes = ["Shared"].
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        builder.AddDbConfig(b =>
        {
            b.Options.Scope = Scope;
            b.Options.Environment = EnvName;
            b.Options.IncludeScopes = [SharedScope];
            b.Options.ReloadInterval = TimeSpan.FromMilliseconds(200);
            b.UseSqlServer(connectionString);
        });

        _app = builder.Build();
        _app.MapDbConfigHttp("/api/dbconfig");

        await _app.StartAsync(ct);

        Client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

[Trait("Category", "E2E")]
[Trait("Category", "SqlServer")]
[Collection(IncludeScopesE2EFixture.CollectionName)]
public sealed class IncludeScopesE2ETests
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;

    public IncludeScopesE2ETests(IncludeScopesE2EFixture fixture)
    {
        _client = fixture.Client;
        _configuration = fixture.Services.GetRequiredService<IConfiguration>();
    }

    [TimedFact(60_000)]
    public async Task SharedScopeWrite_PollingProvider_ReflectsViaIConfiguration()
    {
        // Write a key to the Shared scope via the HTTP API.
        const string key = "IncludeScopesSection/SharedKey";
        const string configKey = "IncludeScopesSection:SharedKey";

        var body = new { value = "shared-value", isSecret = false };

        var putResponse = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{IncludeScopesE2EFixture.SharedScope}/{IncludeScopesE2EFixture.EnvName}/{key}",
            body,
            TestContext.Current.CancellationToken);

        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Poll for up to 5 seconds (reload interval is 200 ms).
        // Allow extra latency for container + polling round-trip.
        await Task.Delay(500, TestContext.Current.CancellationToken);

        var reflected = await EndToEndFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "shared-value", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        reflected.ShouldBeTrue("IConfiguration should reflect the Shared-scope PUT value after polling");
        _configuration[configKey].ShouldBe("shared-value");
    }

    [TimedFact(60_000)]
    public async Task SharedScopeWrite_OwnScopeOverride_OwnValueWins()
    {
        // Write key X to Shared scope, then write the same key to PaymentService scope.
        // PaymentService (own) must win.
        const string key = "Override/Key";
        const string configKey = "Override:Key";

        var sharedBody = new { value = "shared-override", isSecret = false };
        var ownBody = new { value = "own-override", isSecret = false };

        // PUT to shared scope.
        var putShared = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{IncludeScopesE2EFixture.SharedScope}/{IncludeScopesE2EFixture.EnvName}/{key}",
            sharedBody,
            TestContext.Current.CancellationToken);
        putShared.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // PUT to own scope (PaymentService) — done slightly later so watermark advances.
        var putOwn = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{IncludeScopesE2EFixture.Scope}/{IncludeScopesE2EFixture.EnvName}/{key}",
            ownBody,
            TestContext.Current.CancellationToken);
        putOwn.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Poll for up to 5 seconds for the own value to reflect.
        var reflected = await EndToEndFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "own-override", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        reflected.ShouldBeTrue("Own-scope value should override the Shared-scope value in IConfiguration");
        _configuration[configKey].ShouldBe("own-override");
    }

    [TimedFact(60_000)]
    public async Task SharedScopeReloadSignal_ImmediateReflection()
    {
        // Write a key to Shared scope via HTTP PUT, which fires IDbConfigReloadSignal.Trigger()
        // on the server side. Verify reflection happens well within 200 ms (the poll interval).
        const string key = "ReloadSignal/SharedKey";
        const string configKey = "ReloadSignal:SharedKey";

        var body = new { value = "signal-value", isSecret = false };

        var putResponse = await _client.PutAsJsonAsync(
            $"/api/dbconfig/{IncludeScopesE2EFixture.SharedScope}/{IncludeScopesE2EFixture.EnvName}/{key}",
            body,
            TestContext.Current.CancellationToken);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // POST /reload triggers an immediate out-of-band reload — bypasses the 200 ms poll interval.
        var reloadResponse = await _client.PostAsync(
            "/api/dbconfig/reload",
            content: null,
            TestContext.Current.CancellationToken);
        reloadResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Expect reflection within 2 s.
        var reflected = await EndToEndFixture.WaitUntilAsync(
            () => string.Equals(_configuration[configKey], "signal-value", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));

        reflected.ShouldBeTrue(
            "IConfiguration should reflect the Shared-scope PUT value immediately after POST /reload, " +
            "without waiting for the 200 ms polling interval");

        _configuration[configKey].ShouldBe("signal-value");
    }
}
