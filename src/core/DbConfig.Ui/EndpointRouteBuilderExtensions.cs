using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DbConfig.Ui;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> that map the DbConfig embedded UI.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the DbConfig embedded React SPA under <paramref name="prefix"/>. Returns a
    /// <see cref="RouteGroupBuilder"/> so the caller can compose authorization or other policies
    /// (e.g. <c>.RequireAuthorization("DbConfigAdmin")</c>).
    /// </summary>
    public static RouteGroupBuilder MapDbConfigUi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/admin/dbconfig",
        string apiPrefix = "/api/dbconfig")
    {
        var middleware = new EmbeddedStaticFileMiddleware(apiPrefix);
        var group = endpoints.MapGroup(prefix);

        // Serve hashed static assets (JS, CSS, fonts) from the embedded assets/ folder.
        // The EmbeddedFileProvider is sandboxed to the dist/ namespace, so path traversal
        // cannot escape to the host file system.
        group.MapGet("/assets/{**path}", async (HttpContext context, string path) =>
            await middleware.ServeAssetAsync(context, $"/assets/{path}"));

        // Fallback: all other paths under the prefix return index.html (SPA routing).
        group.MapGet("/{**path}", async (HttpContext context) =>
            await middleware.ServeIndexAsync(context));

        // Root of the prefix (no trailing path segment).
        group.MapGet("/", async (HttpContext context) =>
            await middleware.ServeIndexAsync(context));

        return group;
    }
}
