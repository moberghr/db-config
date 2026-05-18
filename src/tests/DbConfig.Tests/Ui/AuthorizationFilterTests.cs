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
/// Verifies <see cref="IDbConfigAuthorizationFilter"/> integration:
/// denial returns 401 by default, 302 to the consumer's redirect when configured.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuthorizationFilterTests
{
    [TimedFact]
    public async Task DenyingFilter_NoRedirect_Returns401()
    {
        await using var app = BuildApp(opts => opts.Authorization = new DenyAllFilter());

        var response = await app.Client.GetAsync(
            "/admin/dbconfig",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TimedFact]
    public async Task DenyingFilter_WithRedirectUrl_RedirectsBrowserWithReturnUrl()
    {
        await using var app = BuildApp(opts =>
        {
            opts.Authorization = new DenyAllFilter();
            opts.UnauthorizedRedirectUrl = "/my-login";
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig/some/page");
        request.Headers.Accept.ParseAdd("text/html");

        var response = await app.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        location.ShouldContain("/my-login");
        location.ShouldContain("returnUrl=");
        location.ShouldContain(Uri.EscapeDataString("/admin/dbconfig/some/page"));
    }

    [TimedFact]
    public async Task AllowingFilter_AllowsRequest()
    {
        await using var app = BuildApp(opts => opts.Authorization = new AllowAllFilter());

        var response = await app.Client.GetAsync(
            "/admin/dbconfig",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static TestApp BuildApp(Action<DbConfigUiOptions> configure)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        var app = builder.Build();
        app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig", configure);

        app.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        var client = app.GetTestClient();

        return new TestApp(app, client);
    }

    private sealed class DenyAllFilter : IDbConfigAuthorizationFilter
    {
        public Task<bool> IsAuthorizedAsync(HttpContext context) => Task.FromResult(false);
    }

    private sealed class AllowAllFilter : IDbConfigAuthorizationFilter
    {
        public Task<bool> IsAuthorizedAsync(HttpContext context) => Task.FromResult(true);
    }

    private sealed class TestApp : IAsyncDisposable
    {
        private readonly WebApplication _app;

        internal TestApp(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        internal HttpClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }
    }
}
