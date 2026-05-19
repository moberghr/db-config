using System.Text.Json;
using DbConfig.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DbConfig.Http.Endpoints;

/// <summary>
/// Flat-query endpoint for the audit log. Returns audit entries across all keys with
/// optional query-string filters so the admin UI can render a global Audit Log page
/// (including audit rows for entries that have since been deleted).
/// </summary>
internal static class QueryAuditEndpoint
{
    private const int DefaultTake = 1000;
    private const int MaxTake = 10000;
    private const int MinTake = 1;

    internal static async Task HandleAsync(
        HttpContext httpContext,
        [FromQuery] string? appName,
        [FromQuery] string? environment,
        [FromQuery] string? tenantId,
        [FromQuery] string? keyPrefix,
        [FromQuery] string? action,
        [FromQuery] int? take,
        [FromServices] IConfigAuditStore? store,
        string? scopeFilter,
        CancellationToken ct)
    {
        // Scope filter enforcement — mirrors QueryEntriesEndpoint.
        var effectiveAppName = appName;
        if (scopeFilter is not null)
        {
            if (string.IsNullOrEmpty(effectiveAppName))
            {
                effectiveAppName = scopeFilter;
            }
            else if (!string.Equals(effectiveAppName, scopeFilter, StringComparison.Ordinal))
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        // Parse the action enum if supplied. Invalid values → 400 with a clear message.
        ConfigAuditAction? parsedAction = null;
        if (!string.IsNullOrEmpty(action))
        {
            if (!Enum.TryParse<ConfigAuditAction>(action, ignoreCase: false, out var actionValue))
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                var error = new
                {
                    error = $"Invalid 'action' value '{action}'. Expected one of: Insert, Update, Delete, Read.",
                };
                await JsonSerializer.SerializeAsync(httpContext.Response.Body, error, JsonOptions.Default, ct);

                return;
            }

            parsedAction = actionValue;
        }

        // Clamp `take` to [MinTake, MaxTake] with DefaultTake when omitted.
        var effectiveTake = take ?? DefaultTake;
        if (effectiveTake < MinTake)
        {
            effectiveTake = MinTake;
        }
        else if (effectiveTake > MaxTake)
        {
            effectiveTake = MaxTake;
        }

        var normalizedAppName = string.IsNullOrEmpty(effectiveAppName) ? null : effectiveAppName;
        var normalizedEnvironment = string.IsNullOrEmpty(environment) ? null : environment;
        var normalizedTenantId = tenantId; // empty string is valid (global-default sentinel)
        var normalizedKeyPrefix = string.IsNullOrEmpty(keyPrefix) ? null : keyPrefix;

        // No audit store registered → return an empty array (the same posture
        // GetAuditHistoryEndpoint takes). The endpoint surface stays stable for clients.
        if (store is null)
        {
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, Array.Empty<ConfigAuditEntry>(), JsonOptions.Default, ct);

            return;
        }

        var entries = await store.QueryAsync(
            normalizedAppName,
            normalizedEnvironment,
            normalizedTenantId,
            normalizedKeyPrefix,
            parsedAction,
            effectiveTake,
            ct);

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, entries, JsonOptions.Default, ct);
    }
}
