using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using DbConfig.Tests.TestData;
using DbConfig.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.Ui;

/// <summary>
/// End-to-end of the built-in cookie login: anonymous GET → 302 to /login,
/// POST /login with valid creds → 302 back with auth cookie, follow-up GET → 200.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BuiltInLoginTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _noRedirectClient;

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
        _noRedirectClient = _app.GetTestServer().CreateClient();
        _noRedirectClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
    }

    public async ValueTask DisposeAsync()
    {
        _noRedirectClient?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }
    }

    [TimedFact]
    public async Task AnonymousRequest_RedirectsToLoginWithReturnUrl()
    {
        var response = await _noRedirectClient!.GetAsync(
            "/admin/dbconfig",
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        location.ShouldContain("/admin/dbconfig/login");
        location.ShouldContain("returnUrl=");
    }

    [TimedFact]
    public async Task LoginGet_RendersForm()
    {
        var response = await _noRedirectClient!.GetAsync(
            "/admin/dbconfig/login",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("<form");
        body.ShouldContain("name='password'");
    }

    [TimedFact]
    public async Task LoginPost_ValidCreds_SetsCookieAndRedirects()
    {
        var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("username", "admin"),
            new KeyValuePair<string, string>("password", "letmein"),
            new KeyValuePair<string, string>("returnUrl", "/admin/dbconfig"),
        ]);

        var response = await _noRedirectClient!.PostAsync(
            "/admin/dbconfig/login",
            form,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/admin/dbconfig");

        var setCookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault() ?? string.Empty;
        setCookie.ShouldContain("dbconfig-auth=");

        // Extract the cookie value and use it to call the protected root again.
        var cookieValue = ExtractCookieValue(setCookie);
        cookieValue.ShouldNotBeNullOrEmpty();

        var authedRequest = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig");
        authedRequest.Headers.Add("Cookie", $"dbconfig-auth={cookieValue}");

        var authed = await _noRedirectClient.SendAsync(
            authedRequest,
            TestContext.Current.CancellationToken);

        authed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await authed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("<html");
    }

    [TimedFact]
    public async Task LoginPost_InvalidCreds_RedirectsToErrorForm()
    {
        var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("username", "admin"),
            new KeyValuePair<string, string>("password", "wrong"),
            new KeyValuePair<string, string>("returnUrl", "/admin/dbconfig"),
        ]);

        var response = await _noRedirectClient!.PostAsync(
            "/admin/dbconfig/login",
            form,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        location.ShouldContain("/admin/dbconfig/login");
        location.ShouldContain("error=1");
    }

    [TimedFact]
    public async Task LoginPost_OpenRedirectAttempt_FallsBackToPrefix()
    {
        var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("username", "admin"),
            new KeyValuePair<string, string>("password", "letmein"),
            new KeyValuePair<string, string>("returnUrl", "//evil.example/path"),
        ]);

        var response = await _noRedirectClient!.PostAsync(
            "/admin/dbconfig/login",
            form,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/admin/dbconfig");
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
