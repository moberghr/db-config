namespace DbConfig.Http;

/// <summary>
/// Options for the DbConfig HTTP API route group. Configure via the
/// <see cref="EndpointRouteBuilderExtensions.MapDbConfigHttp(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder, string, System.Action{DbConfigHttpOptions}?)"/>
/// overload.
/// </summary>
public class DbConfigHttpOptions
{
    /// <summary>
    /// Authorization filter applied to every HTTP API endpoint in the group.
    /// <c>null</c> = open (default). Typically populated automatically by
    /// <c>MapDbConfigAdmin</c> so the same cookie filter that gates the UI
    /// also gates the API.
    /// </summary>
    public IDbConfigAuthorizationFilter? Authorization { get; set; }

    /// <summary>
    /// When non-null, all endpoints in the group enforce that the
    /// <c>{appName}</c> route value matches this value (ordinal comparison).
    /// Requests with a mismatched <c>{appName}</c> receive HTTP 403.
    /// Endpoints that have no <c>{appName}</c> route value (e.g.
    /// <c>POST /reload</c>) are always allowed.
    /// </summary>
    public string? ScopeFilter { get; set; }
}
