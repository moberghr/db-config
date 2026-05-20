using System.Text.Json;
using DbConfig.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DbConfig.Http.Endpoints;

internal static class GetAuditHistoryEndpoint
{
    private const int DefaultTake = 50;
    private const int MaxTake = 500;

    internal static async Task HandleAsync(
        HttpContext httpContext,
        [FromServices] IConfigAuditStore? auditStore,
        [FromServices] ILogger<GetAuditHistoryEndpointMarker>? logger,
        string scope,
        string environment,
        string key,
        int? take,
        CancellationToken ct)
    {
        var requestedTake = take ?? DefaultTake;

        if (requestedTake > MaxTake)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            var error = new { error = $"take must not exceed {MaxTake}." };
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, error, JsonOptions.Default, ct);
            return;
        }

        var normalizedKey = key.Replace('/', ':');
        var tenantIdRaw = httpContext.Request.Query["tenantId"].FirstOrDefault();
        var tenantId = string.IsNullOrEmpty(tenantIdRaw) ? string.Empty : tenantIdRaw;

        IReadOnlyList<ConfigAuditEntry> history;
        if (auditStore is null)
        {
            logger?.LogWarning(
                "IConfigAuditStore is not registered; audit history endpoint returning empty array. " +
                "Did you forget to apply the AddAuditEntries migration or register the audit store?");
            history = [];
        }
        else if (!string.IsNullOrEmpty(tenantId))
        {
            history = await auditStore.GetHistoryForTenantAsync(scope, environment, tenantId, normalizedKey, requestedTake, ct);
        }
        else
        {
            history = await auditStore.GetHistoryAsync(scope, environment, normalizedKey, requestedTake, ct);
        }

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, history, JsonOptions.Default, ct);
    }
}

/// <summary>
/// Marker type used as the category name for <see cref="ILogger"/> in
/// <see cref="GetAuditHistoryEndpoint"/>. Using a dedicated marker avoids a reference to a
/// static class as a generic type argument.
/// </summary>
internal sealed class GetAuditHistoryEndpointMarker;
