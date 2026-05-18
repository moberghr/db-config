using System.Net;
using DbConfig.Tests.TestData;
using DbConfig.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.Ui;

/// <summary>
/// Verifies the ready-made <see cref="LocalRequestsOnlyAuthorizationFilter"/> allows
/// loopback addresses (and TestHost's null RemoteIpAddress) and denies remote IPs.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LocalRequestsOnlyTests
{
    [TimedFact]
    public async Task NullRemoteIp_AllowsAccess()
    {
        var filter = new LocalRequestsOnlyAuthorizationFilter();
        var ctx = new DefaultHttpContext();

        var allowed = await filter.IsAuthorizedAsync(ctx);

        allowed.ShouldBeTrue();
    }

    [TimedFact]
    public async Task LoopbackV4_AllowsAccess()
    {
        var filter = new LocalRequestsOnlyAuthorizationFilter();
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        var allowed = await filter.IsAuthorizedAsync(ctx);

        allowed.ShouldBeTrue();
    }

    [TimedFact]
    public async Task LoopbackV6_AllowsAccess()
    {
        var filter = new LocalRequestsOnlyAuthorizationFilter();
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.IPv6Loopback;

        var allowed = await filter.IsAuthorizedAsync(ctx);

        allowed.ShouldBeTrue();
    }

    [TimedFact]
    public async Task RemoteIp_DeniesAccess()
    {
        var filter = new LocalRequestsOnlyAuthorizationFilter();
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");

        var allowed = await filter.IsAuthorizedAsync(ctx);

        allowed.ShouldBeFalse();
    }

    [TimedFact]
    public async Task EndToEnd_TestHostNullIp_AllowsRequest()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig", opts => opts.Authorization = new LocalRequestsOnlyAuthorizationFilter());

        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/admin/dbconfig",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
