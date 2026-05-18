using System.Net;
using DbConfig.Tests.TestData;
using DbConfig.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.Ui;

/// <summary>
/// Backwards-compatibility coverage for v0.9.0's two-argument MapDbConfigUi overload:
/// no auth filter, no built-in login, the UI is fully open.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpenAccessByDefaultTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        _app = builder.Build();
        _app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");

        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task Root_NoAuthConfigured_Returns200()
    {
        var response = await _client!.GetAsync(
            "/admin/dbconfig",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task LoginEndpoint_NotConfigured_Returns200ForSpaFallback()
    {
        // /login is just a SPA route when built-in login is not enabled.
        var response = await _client!.GetAsync(
            "/admin/dbconfig/login",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("<html");
    }
}
