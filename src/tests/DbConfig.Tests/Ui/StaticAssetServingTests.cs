using System.Net;
using DbConfig.Tests.TestData;
using DbConfig.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace DbConfig.Tests.Ui;

[Trait("Category", "Unit")]
public sealed class StaticAssetServingTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        _app = BuildApp();
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
    public async Task Root_Returns200WithHtmlAndApiPrefixMeta()
    {
        var response = await _client!.GetAsync(
            "/admin/dbconfig",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("<html");
        body.ShouldContain("db-config-api-prefix");
        body.ShouldContain("/api/dbconfig");
    }

    [TimedFact]
    public async Task SpaRoute_Returns200WithHtml()
    {
        var response = await _client!.GetAsync(
            "/admin/dbconfig/some/spa/route",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("<html");
    }

    [TimedFact]
    public async Task JsAsset_Returns200WithJavascriptContentType()
    {
        var jsFileName = FindEmbeddedJsAssetName();
        jsFileName.ShouldNotBeNullOrEmpty("No JS asset found in embedded resources; ensure ui/dist was built before running tests.");

        var response = await _client!.GetAsync(
            $"/admin/dbconfig/assets/{jsFileName}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/javascript");
    }

    [TimedFact]
    public async Task OutsidePrefix_Returns404()
    {
        var response = await _client!.GetAsync(
            "/api/something-else",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TimedFact]
    public async Task PathTraversal_DoesNotExposeSecretContent()
    {
        // HttpClient normalises /assets/../etc/passwd → /admin/dbconfig/etc/passwd (SPA fallback).
        // Even without normalisation the EmbeddedFileProvider is sandboxed to dist/ resources,
        // so the response must never contain file-system content — it is either HTML (SPA
        // fallback) or a 4xx. In either case the body must not look like /etc/passwd.
        var response = await _client!.GetAsync(
            "/admin/dbconfig/assets/../etc/passwd",
            TestContext.Current.CancellationToken);

        var acceptable = response.StatusCode is
            HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.NotFound;
        acceptable.ShouldBeTrue($"Unexpected status {response.StatusCode} for traversal request");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Body must not contain /etc/passwd file contents (root: lines with colons).
        body.ShouldNotContain("root:x:");
    }

    private static string? FindEmbeddedJsAssetName()
    {
        var assembly = typeof(DbConfig.Ui.EndpointRouteBuilderExtensions).Assembly;
        var resources = assembly.GetManifestResourceNames();
        const string assetPrefix = "DbConfig.Ui.dist.assets.";

        foreach (var resource in resources)
        {
            if (resource.Contains(".assets.", StringComparison.Ordinal)
                && resource.EndsWith(".js", StringComparison.Ordinal)
                && resource.StartsWith(assetPrefix, StringComparison.Ordinal))
            {
                // Strip the namespace prefix to get just the filename.
                return resource[assetPrefix.Length..];
            }
        }

        return null;
    }

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        var app = builder.Build();
        app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");

        return app;
    }
}
