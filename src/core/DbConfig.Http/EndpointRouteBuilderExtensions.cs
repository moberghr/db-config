using DbConfig.Core;
using DbConfig.Http.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace DbConfig.Http;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the DbConfig JSON API under <paramref name="prefix"/>. Returns the group builder
    /// so the caller can compose authorization, rate limits, etc. via standard ASP.NET Core
    /// idioms (e.g. <c>.RequireAuthorization("DbConfigAdmin")</c>).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="prefix">The route prefix for the group.</param>
    /// <param name="scopeFilter">
    /// When non-null, all endpoints in the group enforce that the <c>{appName}</c> route value
    /// matches this value (ordinal comparison). Requests with a mismatched <c>{appName}</c>
    /// receive HTTP 403. Endpoints that have no <c>{appName}</c> route value (e.g.
    /// <c>POST /reload</c>) are always allowed.
    /// </param>
    public static RouteGroupBuilder MapDbConfigHttp(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api/dbconfig",
        string? scopeFilter = null)
    {
        Action<DbConfigHttpOptions>? configure = scopeFilter is null
            ? null
            : o => o.ScopeFilter = scopeFilter;

        return MapDbConfigHttp(endpoints, prefix, configure);
    }

    /// <summary>
    /// Maps the DbConfig JSON API under <paramref name="prefix"/> with optional configuration.
    /// When <paramref name="configure"/> is <c>null</c> the behavior is identical to the
    /// two-argument overload (open access, no scope filter).
    /// </summary>
    /// <remarks>
    /// Use this overload to wire an <see cref="IDbConfigAuthorizationFilter"/> directly onto
    /// the HTTP API route group — typically the cookie filter shared with <c>MapDbConfigUi</c>
    /// in unified-mount scenarios. <c>MapDbConfigAdmin</c> calls this internally.
    /// </remarks>
    public static RouteGroupBuilder MapDbConfigHttp(
        this IEndpointRouteBuilder endpoints,
        string prefix,
        Action<DbConfigHttpOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new DbConfigHttpOptions();
        configure?.Invoke(options);

        var group = endpoints.MapGroup(prefix);

        if (options.Authorization is not null)
        {
            var capturedAuth = options.Authorization;
            group.AddEndpointFilter(async (context, next) =>
            {
                var authorized = await capturedAuth.IsAuthorizedAsync(context.HttpContext);
                if (!authorized)
                {
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);
                }

                return await next(context);
            });
        }

        if (options.ScopeFilter is not null)
        {
            var capturedFilter = options.ScopeFilter;
            group.AddEndpointFilter(async (context, next) =>
            {
                var routeAppName = context.HttpContext.Request.RouteValues["appName"] as string;

                // No appName in route (e.g. POST /reload) — always allowed.
                if (routeAppName is null)
                {
                    return await next(context);
                }

                // appName must match the configured scope filter (ordinal comparison).
                if (!string.Equals(routeAppName, capturedFilter, StringComparison.Ordinal))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                return await next(context);
            });
        }

        // Flat-query endpoint at the group root. Closure-captures the scopeFilter so the
        // endpoint can enforce it without depending on the route-level filter (which runs
        // off the {appName} route value — absent here because we use query strings).
        var capturedScopeFilter = options.ScopeFilter;
        group.MapGet("/", (
            HttpContext httpContext,
            [FromQuery] string? appName,
            [FromQuery] string? environment,
            [FromQuery] string? tenantId,
            [FromQuery] string? keyPrefix,
            [FromQuery] int? take,
            IConfigStore store,
            [FromServices] IConfigAuditStore? auditStore,
            [FromServices] DbConfigOptions? dbOptions,
            [FromServices] ILogger<QueryEntriesEndpointMarker>? logger,
            [FromServices] TimeProvider? timeProvider,
            CancellationToken ct) => QueryEntriesEndpoint.HandleAsync(
                httpContext,
                appName,
                environment,
                tenantId,
                keyPrefix,
                take,
                store,
                capturedScopeFilter,
                auditStore,
                dbOptions,
                logger,
                timeProvider,
                ct));

        group.MapGet("/{appName}/{environment}", ListEntriesEndpoint.HandleAsync);
        group.MapGet("/{appName}/{environment}/audit/{**key}", GetAuditHistoryEndpoint.HandleAsync);
        group.MapGet("/{appName}/{environment}/{*key}", GetEntryEndpoint.HandleAsync);
        group.MapPut("/{appName}/{environment}/{*key}", UpsertEntryEndpoint.HandleAsync);
        group.MapDelete("/{appName}/{environment}/{*key}", DeleteEntryEndpoint.HandleAsync);
        group.MapPost("/reload", ReloadEndpoint.Handle);

        return group;
    }
}
