using System.Net;
using DbConfig.Tests.TestData;
using DbConfig.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.Ui;

/// <summary>
/// Verifies the v0.9.0 two-argument MapDbConfigUi overload still composes with
/// ASP.NET Core's standard <c>RequireAuthorization</c> pipeline. The host can
/// continue to own auth without touching the new built-in options.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BackwardsCompatTests
{
    [TimedFact]
    public async Task TwoArgOverload_ReturnsRouteGroupBuilder_RequireAuthorization_StillWorks()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddAuthentication("Test").AddCookie("Test");
        builder.Services.AddAuthorization(o => o.AddPolicy("DbConfigAdmin", p => p.RequireAssertion(_ => false)));

        await using var app = builder.Build();
        app.UseAuthorization();

        var group = app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");
        group.ShouldBeOfType<RouteGroupBuilder>();
        group.RequireAuthorization("DbConfigAdmin");

        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        // RequireAuthorization with a policy that always fails should reject the request.
        // Cookie scheme defaults to a 302 to /Account/Login on challenge; other schemes
        // return 401/403. All three indicate the policy was actually enforced.
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig");
        var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        var enforced = response.StatusCode
            is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.Found
            or HttpStatusCode.Redirect;
        enforced.ShouldBeTrue($"Expected policy enforcement (401/403/302), got {response.StatusCode}");
    }
}
