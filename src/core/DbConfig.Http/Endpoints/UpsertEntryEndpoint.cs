using System.Text.Json;
using DbConfig.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DbConfig.Http.Endpoints;

internal static class UpsertEntryEndpoint
{
    internal static async Task<IResult> HandleAsync(
        string scope,
        string environment,
        string key,
        UpsertEntryRequest body,
        IConfigStore store,
        IDbConfigReloadSignal reloadSignal,
        TimeProvider timeProvider,
        HttpContext httpContext,
        [FromServices] ILogger<UpsertEntryEndpointMarker>? logger,
        CancellationToken ct)
    {
        var normalizedKey = key.Replace('/', ':');
        var modifiedBy = httpContext.User?.Identity?.Name;
        var now = timeProvider.GetUtcNow();

        var tenantId = string.IsNullOrEmpty(body.TenantId) ? string.Empty : body.TenantId;

        // Read the current entry only when the request depends on it. The read is outside the
        // store's write, so a concurrent flag change in between is last-writer-wins.
        var existing = body.IsWriteOnly is null || body.Value is null
            ? await GetExistingAsync(store, scope, environment, tenantId, normalizedKey, ct)
            : null;

        if (existing?.IsWriteOnly == true && body.Value is null)
        {
            return JsonError(StatusCodes.Status400BadRequest, "A write-only entry cannot be set to null. Delete the entry instead.");
        }

        var isWriteOnly = body.IsWriteOnly ?? existing?.IsWriteOnly ?? false;

        var entry = new ConfigEntryRecord(
            scope,
            environment,
            tenantId,
            normalizedKey,
            body.Value,
            body.IsSecret || isWriteOnly,
            now,
            modifiedBy,
            isWriteOnly);

        await store.UpsertAsync(entry, ct);
        reloadSignal.Trigger();

        // A store that drops IsWriteOnly would make the HTTP layer serve the value in plaintext.
        // Fail loudly instead of answering 204 for a write that is not actually write-only.
        if (isWriteOnly)
        {
            var stored = await GetExistingAsync(store, scope, environment, tenantId, normalizedKey, ct);
            if (stored is { IsWriteOnly: false })
            {
                logger?.LogError(
                    "The registered IConfigStore ({StoreType}) did not persist IsWriteOnly for {Key}. " +
                    "Write-only values will be served by the HTTP API until the store supports the flag.",
                    store.GetType().Name,
                    normalizedKey);

                return JsonError(StatusCodes.Status500InternalServerError, "The configuration store does not support write-only entries.");
            }
        }

        return Results.NoContent();
    }

    // Serialized by hand like the other endpoints' error bodies: TestServer's response PipeWriter
    // does not support the PipeWriter overloads Results.Json / Results.Problem use.
    private static IResult JsonError(int statusCode, string error)
    {
        var body = JsonSerializer.Serialize(new { error }, JsonOptions.Default);

        return Results.Text(body, "application/json; charset=utf-8", statusCode: statusCode);
    }

    private static async Task<ConfigEntryRecord?> GetExistingAsync(
        IConfigStore store, string scope, string environment, string tenantId, string key, CancellationToken ct)
    {
        return string.IsNullOrEmpty(tenantId)
            ? await store.GetAsync(scope, environment, key, ct)
            : await store.GetForTenantAsync(scope, environment, tenantId, key, ct);
    }
}

/// <summary>
/// Marker type used as the category name for <see cref="ILogger"/> in
/// <see cref="UpsertEntryEndpoint"/>. Using a dedicated marker avoids a reference to a
/// static class as a generic type argument.
/// </summary>
internal sealed class UpsertEntryEndpointMarker;
