using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DbConfig.Core;
using DbConfig.Http;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace DbConfig.Tests.Http;

[Trait("Category", "Unit")]
public sealed class EndpointAuthCompositionTests
{
    private const string App = "AuthTestApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task GetEntries_WithRequireAuthorization_Returns401WhenUnauthenticated()
    {
        await using var app = BuildApp(requireAuth: true);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/?scope={App}&environment={Env}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TimedFact]
    public async Task GetEntries_WithRequireAuthorization_Returns200WhenAuthenticated()
    {
        await using var app = BuildApp(requireAuth: true);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var store = app.Services.GetRequiredService<IConfigStore>();
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "SomeKey", "val", false, DateTimeOffset.UtcNow, null),
            TestContext.Current.CancellationToken);

        // The fake scheme authenticates when the header is present.
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");

        var response = await client.GetAsync(
            $"/api/dbconfig/?scope={App}&environment={Env}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task GetEntries_WithoutRequireAuthorization_Returns200Anonymous()
    {
        await using var app = BuildApp(requireAuth: false);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/dbconfig/?scope={App}&environment={Env}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static WebApplication BuildApp(bool requireAuth)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfigStore, InMemoryConfigStore>();
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                "DbConfigAdmin",
                policy => policy.RequireAuthenticatedUser());
        });
        builder.Services.AddAuthentication("Fake")
            .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>("Fake", _ => { });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        var group = app.MapDbConfigHttp("/api/dbconfig");
        if (requireAuth)
        {
            group.RequireAuthorization("DbConfigAdmin");
        }

        return app;
    }

    private sealed class NoOpReloadSignal : IDbConfigReloadSignal
    {
        public void Trigger()
        {
        }
    }

    private sealed class FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public FakeAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Auth"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity("Fake");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Fake");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
