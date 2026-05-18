using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using DbConfig.Core;
using DbConfig.Http;
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
/// Unified-mount tests: <see cref="MapDbConfigAdminExtensions.MapDbConfigAdmin"/>
/// mounts the UI and HTTP API under one prefix with one cookie filter.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MapDbConfigAdminTests
{
    [TimedFact]
    public async Task UnifiedMount_UiAndApi_BothUseSameCookie()
    {
        await using var app = await BuildAppAsync(opts => opts.UseBuiltInLogin<FakeValidator>());

        // 1. Sign in via the UI's built-in login.
        var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("username", "admin"),
            new KeyValuePair<string, string>("password", "letmein"),
            new KeyValuePair<string, string>("returnUrl", "/admin/dbconfig"),
        ]);

        var loginResponse = await app.Client.PostAsync(
            "/admin/dbconfig/login",
            form,
            TestContext.Current.CancellationToken);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var setCookie = loginResponse.Headers.GetValues("Set-Cookie").FirstOrDefault() ?? string.Empty;

        var cookieValue = ExtractCookieValue(setCookie);
        cookieValue.ShouldNotBeNullOrEmpty();

        // The cookie Path MUST cover the prefix (so the API at /admin/dbconfig/api inherits it).
        setCookie.ShouldContain("path=/admin/dbconfig", Case.Insensitive);

        // 2. UI request with cookie → 200.
        var uiRequest = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig");
        uiRequest.Headers.Add("Cookie", $"dbconfig-auth={cookieValue}");
        var uiResponse = await app.Client.SendAsync(uiRequest, TestContext.Current.CancellationToken);
        uiResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. API request with the SAME cookie → 200 (no separate auth dance).
        var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig/api/MyApp/Production");
        apiRequest.Headers.Add("Cookie", $"dbconfig-auth={cookieValue}");
        var apiResponse = await app.Client.SendAsync(apiRequest, TestContext.Current.CancellationToken);

        // The configuration store isn't wired in this test, so the endpoint will 500 — but
        // the important bit is that the AUTH filter let the request through to the handler.
        // 401 here would mean the cookie didn't authorize the API. Accept anything BUT 401.
        apiResponse.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [TimedFact]
    public async Task UnifiedMount_WithoutLogin_BothRedirect()
    {
        await using var app = await BuildAppAsync(opts => opts.UseBuiltInLogin<FakeValidator>());

        // Browser-style request (Accept: text/html) → 302 to the login page.
        var uiRequest = new HttpRequestMessage(HttpMethod.Get, "/admin/dbconfig");
        uiRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var uiResponse = await app.Client.SendAsync(
            uiRequest,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        uiResponse.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        uiResponse.Headers.Location?.OriginalString.ShouldContain("/admin/dbconfig/login");

        // API request without cookie → 401 (the HTTP surface doesn't redirect; it returns 401).
        var apiResponse = await app.Client.GetAsync(
            "/admin/dbconfig/api/MyApp/Production",
            TestContext.Current.CancellationToken);

        apiResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TimedFact]
    public async Task UnifiedMount_NoConfigure_BothOpen()
    {
        await using var app = await BuildAppAsync(configure: null);

        // No auth wired — UI request returns 200.
        var uiResponse = await app.Client.GetAsync(
            "/admin/dbconfig",
            TestContext.Current.CancellationToken);
        uiResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // API request — also reaches the handler unauthenticated (will 500 because no store
        // is registered, but it's NOT a 401).
        var apiResponse = await app.Client.GetAsync(
            "/admin/dbconfig/api/MyApp/Production",
            TestContext.Current.CancellationToken);
        apiResponse.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [TimedFact]
    public async Task UnifiedMount_ApiRespectsAuthorizationFilter()
    {
        await using var app = await BuildAppAsync(opts => opts.Authorization = new DenyAllFilter());

        // Both UI and API must be rejected by the shared filter.
        var uiResponse = await app.Client.GetAsync(
            "/admin/dbconfig",
            TestContext.Current.CancellationToken);
        uiResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var apiResponse = await app.Client.GetAsync(
            "/admin/dbconfig/api/MyApp/Production",
            TestContext.Current.CancellationToken);
        apiResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<TestApp> BuildAppAsync(Action<DbConfigUiOptions>? configure)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddDataProtection();
        builder.Services.AddScoped<IDbConfigCredentialValidator, FakeValidator>();

        // HTTP API endpoints require IConfigStore + reload signal + TimeProvider to bind.
        // The auth-filter tests only care whether the endpoint is REACHABLE; an empty
        // in-memory store and no-op signal are enough.
        builder.Services.AddSingleton<IConfigStore>(new InMemoryConfigStore());
        builder.Services.AddSingleton<IDbConfigReloadSignal, NoOpReloadSignal>();
        builder.Services.AddSingleton(TimeProvider.System);

        var app = builder.Build();
        app.MapDbConfigAdmin("/admin/dbconfig", configure);

        await app.StartAsync(TestContext.Current.CancellationToken);

        var client = app.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        return new TestApp(app, client);
    }

    private static string? ExtractCookieValue(string setCookieHeader)
    {
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

    private sealed class DenyAllFilter : IDbConfigAuthorizationFilter
    {
        public Task<bool> IsAuthorizedAsync(HttpContext context) => Task.FromResult(false);
    }

    private sealed class NoOpReloadSignal : IDbConfigReloadSignal
    {
        public void Trigger()
        {
        }
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
