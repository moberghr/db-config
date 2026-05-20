using DbConfig.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        AutoWireCookieAuthFilter(endpoints, options);

        return MapUiInternal(endpoints, prefix, apiPrefix, options);
    }

    /// <summary>
    /// When the consumer enables built-in cookie login and didn't supply their own
    /// authorization filter, wire up <see cref="CookieAuthorizationFilter"/> automatically.
    /// Shared between <see cref="MapDbConfigUi(IEndpointRouteBuilder, string, string, Action{DbConfigUiOptions}?)"/>
    /// and <see cref="MapDbConfigAdminExtensions.MapDbConfigAdmin"/>.
    /// </summary>
    internal static void AutoWireCookieAuthFilter(IEndpointRouteBuilder endpoints, DbConfigUiOptions options)
    {
        if (options.CredentialValidatorType is null || options.Authorization is not null)
        {
            return;
        }

        var protector = endpoints.ServiceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(BuiltInLoginEndpoints.ProtectorPurpose);

        options.Authorization = new CookieAuthorizationFilter(protector, options.CookieName);
    }

    internal static RouteGroupBuilder MapUiInternal(
        IEndpointRouteBuilder endpoints,
        string prefix,
        string apiPrefix,
        DbConfigUiOptions options)
    {
        var middleware = new EmbeddedStaticFileMiddleware(prefix, apiPrefix, options.CredentialValidatorType is not null);
        var group = endpoints.MapGroup(prefix);

        // Auth filter runs first for every endpoint in the group. When no Authorization is
        // configured, the filter is a no-op (it still excludes /login + /logout from the
        // check, which only exist when built-in login is enabled).
        if (options.Authorization is not null || options.CredentialValidatorType is not null)
        {
            group.AddEndpointFilter(new DbConfigUiAuthFilter(options, prefix));
        }

        // Built-in login endpoints — registered only when UseBuiltInLogin<T>() was called.
        // The login UI itself is rendered by the React SPA: the catch-all serves index.html
        // for /login. These JSON endpoints expose the contract the SPA calls. /api/auth/status
        // MUST be reachable without a valid cookie so the SPA can decide whether to render
        // the login page; the auth filter exempts /api/auth/* paths for the same reason
        // /login + /logout were exempted in the server-rendered design.
        if (options.CredentialValidatorType is not null)
        {
            group.MapGet("/api/auth/status", async (HttpContext ctx) =>
                await BuiltInLoginEndpoints.HandleAuthStatusAsync(ctx, options));

            group.MapPost("/api/auth/login", async (HttpContext ctx) =>
                await BuiltInLoginEndpoints.HandleLoginPostAsync(ctx, options, prefix));

            group.MapPost("/api/auth/logout", async (HttpContext ctx) =>
                await BuiltInLoginEndpoints.HandleLogoutPostAsync(ctx, options, prefix));
        }

        // ASP.NET's built-in StaticFileMiddleware handles every file in the embedded UI
        // bundle (assets, fonts, favicon, anything Vite emits) with correct Content-Type,
        // ETag, Last-Modified, conditional GETs, range requests, and cache headers. When
        // the request doesn't match a file in the bundle, the middleware falls through to
        // the SPA index — that's the `next` delegate passed below.
        var staticFileMiddleware = CreateStaticFileMiddleware(
            endpoints.ServiceProvider,
            middleware,
            prefix);

        // Single catch-all: try the static-file middleware first; if no file matches it
        // calls `next`, which serves index.html (SPA fallback).
        group.MapGet("/{**path}", (HttpContext context) =>
            InvokeStaticFileOrFallbackAsync(staticFileMiddleware, context));

        // Root of the prefix (no trailing path segment) always serves the SPA index.
        group.MapGet("/", async (HttpContext context) =>
            await middleware.ServeIndexAsync(context));

        // Browser auto-requests for /favicon.ico (legacy / Edge / IE) are aliased to the
        // SVG favicon. StaticFileMiddleware wouldn't otherwise serve the .ico path because
        // there is no .ico file in the bundle.
        group.MapGet("/favicon.ico", (HttpContext context) =>
        {
            context.Request.Path = $"{prefix}/favicon.svg";

            return InvokeStaticFileOrFallbackAsync(staticFileMiddleware, context);
        });

        return group;
    }

    private static StaticFileMiddleware CreateStaticFileMiddleware(
        IServiceProvider services,
        EmbeddedStaticFileMiddleware indexRenderer,
        string prefix)
    {
        var hostingEnv = services.GetRequiredService<IWebHostEnvironment>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        var staticFileOptions = new StaticFileOptions
        {
            RequestPath = prefix,
            FileProvider = indexRenderer.FileProvider,
            ContentTypeProvider = BuildContentTypeProvider(),
        };

        // `next` (invoked when no embedded file matches) is the SPA fallback — every
        // unknown path under the UI prefix returns the rewritten index.html so client-side
        // routing can take over.
        var spaFallback = (RequestDelegate)(context => indexRenderer.ServeIndexAsync(context));

        return new StaticFileMiddleware(
            spaFallback,
            hostingEnv,
            Options.Create(staticFileOptions),
            loggerFactory);
    }

    // The framework's default content-type provider returns "text/javascript" for .js per
    // the current IANA recommendation. The v0.10.0 hand-rolled provider used the older
    // "application/javascript" — keep that to preserve byte-identical Content-Type headers
    // for existing consumers.
    private static FileExtensionContentTypeProvider BuildContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".js"] = "application/javascript";

        return provider;
    }

    // StaticFileMiddleware short-circuits to `next` when it sees the request has already
    // matched a routing endpoint (it's designed to run before endpoint routing). Because
    // we host it inside a route-group endpoint to compose with the consumer's auth policy,
    // we clear the matched endpoint for the duration of the static-file lookup. If the
    // middleware doesn't find a file it calls `next` (our SPA fallback) directly.
    private static async Task InvokeStaticFileOrFallbackAsync(StaticFileMiddleware middleware, HttpContext context)
    {
        var originalEndpoint = context.GetEndpoint();
        context.SetEndpoint(null);

        try
        {
            await middleware.Invoke(context);
        }
        finally
        {
            context.SetEndpoint(originalEndpoint);
        }
    }
}
