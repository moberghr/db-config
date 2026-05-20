using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DbConfig.Http;
using DbConfig.Tests.TestData;
using DbConfig.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.Ui;

/// <summary>
/// End-to-end of the built-in cookie login under the v0.11.0 React-rendered design:
/// the SPA shell handles the login UI; the backend exposes a JSON contract for
/// status / login / logout under <c>/api/auth/*</c>. The catch-all serves
/// <c>index.html</c> for <c>/login</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BuiltInLoginTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddDataProtection();
        builder.Services.AddScoped<IDbConfigCredentialValidator, FakeValidator>();

        _app = builder.Build();
        _app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig", opts => opts.UseBuiltInLogin<FakeValidator>());

        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = _app.GetTestServer().CreateClient();
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
    public async Task LoginGet_NowServesSpaIndexHtml()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig/login");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await _client!.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("<html");
        body.ShouldContain("window.dbConfig");
        body.ShouldContain("hasBuiltInLogin: true");
    }

    [TimedFact]
    public async Task LoginPost_ValidCredentials_ReturnsOkWithCookie()
    {
        var response = await _client!.PostAsJsonAsync(
            "/admin/dbconfig/api/auth/login",
            new { username = "admin", password = "letmein" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var setCookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault() ?? string.Empty;
        setCookie.ShouldContain("dbconfig-auth=");

        var cookieValue = ExtractCookieValue(setCookie);
        cookieValue.ShouldNotBeNullOrEmpty();

        // Re-issue an authed request and confirm the cookie unlocks the UI.
        var authedRequest = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig");
        authedRequest.Headers.Add("Cookie", $"dbconfig-auth={cookieValue}");

        var authed = await _client!.SendAsync(
            authedRequest,
            TestContext.Current.CancellationToken);

        authed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TimedFact]
    public async Task LoginPost_InvalidCredentials_Returns401WithJsonError()
    {
        var response = await _client!.PostAsJsonAsync(
            "/admin/dbconfig/api/auth/login",
            new { username = "admin", password = "wrong" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetString().ShouldNotBeNullOrEmpty();
    }

    [TimedFact]
    public async Task Logout_ClearsCookieAndReturnsOk()
    {
        var response = await _client!.PostAsync(
            "/admin/dbconfig/api/auth/logout",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        // Delete cookie semantics: emit a Set-Cookie that expires the cookie.
        var setCookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault() ?? string.Empty;
        setCookie.ShouldContain("dbconfig-auth=");
    }

    [TimedFact]
    public async Task AuthStatus_Unauthenticated_ReturnsAuthenticatedFalseWithHasBuiltInLoginTrue()
    {
        var response = await _client!.GetAsync(
            "/admin/dbconfig/api/auth/status",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("authenticated").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("hasBuiltInLogin").GetBoolean().ShouldBeTrue();
    }

    [TimedFact]
    public async Task AuthStatus_AfterLogin_ReturnsAuthenticatedTrueWithUsername()
    {
        // Sign in to acquire a cookie.
        var login = await _client!.PostAsJsonAsync(
            "/admin/dbconfig/api/auth/login",
            new { username = "admin", password = "letmein" },
            TestContext.Current.CancellationToken);

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookieValue = ExtractCookieValue(login.Headers.GetValues("Set-Cookie").First());
        cookieValue.ShouldNotBeNullOrEmpty();

        // Hit status with the cookie attached.
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig/api/auth/status");
        request.Headers.Add("Cookie", $"dbconfig-auth={cookieValue}");

        var response = await _client!.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("authenticated").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("hasBuiltInLogin").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("username").GetString().ShouldBe("admin");
    }

    [TimedFact]
    public async Task BrowserNavigation_Unauthenticated_ServesSpaShellNotRedirect()
    {
        // v0.11.0: browser GETs no longer 302 to /login — the SPA shell renders and
        // the React app calls /api/auth/status to decide whether to show LoginPage.
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await _client!.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    private static string? ExtractCookieValue(string setCookieHeader)
    {
        // Expect "dbconfig-auth=<value>; Path=...; ..."
        const string Name = "dbconfig-auth=";
        var nameIndex = setCookieHeader.IndexOf(Name, StringComparison.Ordinal);
        if (nameIndex < 0)
        {
            return null;
        }

        var valueStart = nameIndex + Name.Length;
        var valueEnd = setCookieHeader.IndexOf(';', valueStart);

        return valueEnd < 0
            ? setCookieHeader[valueStart..]
            : setCookieHeader[valueStart..valueEnd];
    }

    internal sealed class FakeValidator : IDbConfigCredentialValidator
    {
        public Task<ClaimsPrincipal?> ValidateAsync(string username, string password, CancellationToken ct)
        {
            if (!string.Equals(password, "letmein", StringComparison.Ordinal))
            {
                return Task.FromResult<ClaimsPrincipal?>(null);
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, username)],
                "DbConfigTest");

            return Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(identity));
        }
    }
}
