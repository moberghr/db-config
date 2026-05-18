using DbConfig.Http;
using Microsoft.AspNetCore.Routing;

namespace DbConfig.Ui;

/// <summary>
/// Unified mount that places the DbConfig admin UI and HTTP API under a single
/// route prefix with a shared authorization filter. Mirrors sister project
/// Warp's <c>UseWarpUI</c> one-call pattern.
/// </summary>
public static class MapDbConfigAdminExtensions
{
    /// <summary>
    /// Mounts both the DbConfig admin UI (at <paramref name="prefix"/>) and the
    /// HTTP API (at <c>{prefix}/api</c>) with a shared authorization filter.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="prefix">Mount prefix, e.g. <c>"/admin/dbconfig"</c>. UI
    /// serves at this path; the HTTP API serves at <c>"{prefix}/api"</c>.</param>
    /// <param name="configure">Optional configuration callback. When omitted,
    /// both surfaces are open (no auth) — same as the v0.9.0 defaults of the
    /// individual <c>MapDbConfigUi</c> / <c>MapDbConfigHttp</c> calls.</param>
    /// <returns>Both group builders so consumers can chain per-surface
    /// customizations (e.g. additional endpoint filters) if needed.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddScoped&lt;IDbConfigCredentialValidator, MyValidator&gt;();
    /// app.MapDbConfigAdmin("/admin/dbconfig", opts =>
    /// {
    ///     opts.UseBuiltInLogin&lt;MyValidator&gt;();
    /// });
    /// </code>
    /// </example>
    public static DbConfigAdminEndpoints MapDbConfigAdmin(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/admin/dbconfig",
        Action<DbConfigUiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(prefix);

        var apiPrefix = $"{prefix}/api";

        var options = new DbConfigUiOptions();
        configure?.Invoke(options);

        // The cookie must cover both the UI (prefix) AND the sibling API (prefix/api).
        // Setting Path = prefix achieves that because /admin/dbconfig is a parent of
        // /admin/dbconfig/api. Consumers can override CookiePath in the callback if
        // they want a wider scope (e.g. "/").
        options.CookiePath ??= prefix;

        EndpointRouteBuilderExtensions.AutoWireCookieAuthFilter(endpoints, options);

        var uiGroup = EndpointRouteBuilderExtensions.MapUiInternal(endpoints, prefix, apiPrefix, options);

        var apiGroup = endpoints.MapDbConfigHttp(apiPrefix, http =>
        {
            // Share the (possibly auto-wired) cookie filter so /api endpoints reject
            // unauthenticated callers the same way the UI does.
            http.Authorization = options.Authorization;
        });

        return new DbConfigAdminEndpoints(uiGroup, apiGroup);
    }
}

/// <summary>
/// The two route groups produced by <see cref="MapDbConfigAdminExtensions.MapDbConfigAdmin"/>.
/// </summary>
public sealed record DbConfigAdminEndpoints(RouteGroupBuilder Ui, RouteGroupBuilder Api);
