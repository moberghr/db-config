using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DbConfig.Ui;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> that map the DbConfig embedded UI.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the DbConfig embedded React SPA under <paramref name="prefix"/>. Returns a
    /// <see cref="RouteGroupBuilder"/> so the caller can compose authorization or other
    /// policies (e.g. <c>.RequireAuthorization("DbConfigAdmin")</c>).
    /// </summary>
    /// <remarks>
    /// This overload is the v0.9.0 shape and preserves open-access semantics — no
    /// auth filter, no login endpoints. Hosts that want the built-in auth surface
    /// should call the <see cref="MapDbConfigUi(IEndpointRouteBuilder, string, string, Action{DbConfigUiOptions}?)"/>
    /// overload instead.
    /// </remarks>
    public static RouteGroupBuilder MapDbConfigUi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/admin/dbconfig",
        string apiPrefix = "/api/dbconfig")
    {
        return MapDbConfigUi(endpoints, prefix, apiPrefix, configure: null);
    }

    /// <summary>
    /// Maps the DbConfig embedded React SPA under <paramref name="prefix"/> with optional
    /// configuration. When <paramref name="configure"/> is <c>null</c> the behavior is
    /// identical to the two-argument overload (open access, no built-in auth).
    /// </summary>
    /// <remarks>
    /// To enable the built-in cookie login, call <c>opts.UseBuiltInLogin&lt;TValidator&gt;()</c>
    /// inside <paramref name="configure"/> AND register the validator in DI before this
    /// method is called:
    /// <code>
    /// builder.Services.AddScoped&lt;IDbConfigCredentialValidator, MyValidator&gt;();
    /// app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig", opts =>
    /// {
    ///     opts.UseBuiltInLogin&lt;MyValidator&gt;();
    /// });
    /// </code>
    /// </remarks>
    public static RouteGroupBuilder MapDbConfigUi(
        this IEndpointRouteBuilder endpoints,
        string prefix,
        string apiPrefix,
        Action<DbConfigUiOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new DbConfigUiOptions();
        configure?.Invoke(options);

        // When the consumer enables built-in cookie login, auto-wire a CookieAuthorizationFilter
        // unless they also supplied their own. This mirrors Warp's pattern where the cookie
        // check is the implicit authorization mechanism for the built-in flow.
        if (options.CredentialValidatorType is not null && options.Authorization is null)
        {
            var protector = endpoints.ServiceProvider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(BuiltInLoginEndpoints.ProtectorPurpose);

            options.Authorization = new CookieAuthorizationFilter(protector, options.CookieName);
        }

        var middleware = new EmbeddedStaticFileMiddleware(apiPrefix);
        var group = endpoints.MapGroup(prefix);

        // Auth filter runs first for every endpoint in the group. When no Authorization is
        // configured, the filter is a no-op (it still excludes /login + /logout from the
        // check, which only exist when built-in login is enabled).
        if (options.Authorization is not null || options.CredentialValidatorType is not null)
        {
            group.AddEndpointFilter(new DbConfigUiAuthFilter(options, prefix));
        }

        // Built-in login endpoints — registered only when UseBuiltInLogin<T>() was called.
        if (options.CredentialValidatorType is not null)
        {
            group.MapGet("/login", async (HttpContext ctx) =>
                await BuiltInLoginEndpoints.HandleLoginGetAsync(ctx, options, prefix));

            group.MapPost("/login", async (HttpContext ctx) =>
                await BuiltInLoginEndpoints.HandleLoginPostAsync(ctx, options, prefix));

            group.MapPost("/logout", async (HttpContext ctx) =>
                await BuiltInLoginEndpoints.HandleLogoutPostAsync(ctx, options, prefix));
        }

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
