using DbConfig.Http.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
        var group = endpoints.MapGroup(prefix);

        if (scopeFilter is not null)
        {
            var capturedFilter = scopeFilter;
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

        group.MapGet("/{appName}/{environment}", ListEntriesEndpoint.HandleAsync);
        group.MapGet("/{appName}/{environment}/audit/{**key}", GetAuditHistoryEndpoint.HandleAsync);
        group.MapGet("/{appName}/{environment}/{*key}", GetEntryEndpoint.HandleAsync);
        group.MapPut("/{appName}/{environment}/{*key}", UpsertEntryEndpoint.HandleAsync);
        group.MapDelete("/{appName}/{environment}/{*key}", DeleteEntryEndpoint.HandleAsync);
        group.MapPost("/reload", ReloadEndpoint.Handle);

        return group;
    }
}
