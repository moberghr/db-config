using System.Text.Json;
using System.Threading;
using DbConfig.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DbConfig.Http.Endpoints;

internal static class GetEntryEndpoint
{
    private static int _warnedAboutMissingStore;

    internal static async Task HandleAsync(
        string appName,
        string environment,
        string key,
        IConfigStore store,
        HttpContext httpContext,
        [FromServices] IConfigAuditStore? auditStore,
        [FromServices] DbConfigOptions? options,
        [FromServices] ILogger<GetEntryEndpointMarker>? logger,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken ct)
    {
        var normalizedKey = key.Replace('/', ':');
        var tenantIdRaw = httpContext.Request.Query["tenantId"].FirstOrDefault();
        var tenantId = string.IsNullOrEmpty(tenantIdRaw) ? string.Empty : tenantIdRaw;
        var fallbackRaw = httpContext.Request.Query["fallback"].FirstOrDefault();
        var fallback = string.Equals(fallbackRaw, "true", StringComparison.OrdinalIgnoreCase);

        ConfigEntry? entry;

        if (string.IsNullOrEmpty(tenantId))
        {
            entry = await store.GetAsync(appName, environment, normalizedKey, ct);
        }
        else
        {
            entry = await store.GetForTenantAsync(appName, environment, tenantId, normalizedKey, ct);

            if (entry is null && fallback)
            {
                entry = await store.GetAsync(appName, environment, normalizedKey, ct);
            }
        }

        if (entry is null)
        {
            // Still write a read audit row for 404 — access attempt is worth recording.
            WriteReadAudit(appName, environment, normalizedKey, httpContext, auditStore, options, logger, timeProvider);

            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, entry, JsonOptions.Default, ct);

        WriteReadAudit(appName, environment, normalizedKey, httpContext, auditStore, options, logger, timeProvider);
    }

    private static void WriteReadAudit(
        string appName,
        string environment,
        string normalizedKey,
        HttpContext httpContext,
        IConfigAuditStore? auditStore,
        DbConfigOptions? options,
        ILogger<GetEntryEndpointMarker>? logger,
        TimeProvider? timeProvider)
    {
        if (options is null || !options.AuditReads)
        {
            return;
        }

        if (auditStore is null)
        {
            if (Interlocked.CompareExchange(ref _warnedAboutMissingStore, 1, 0) == 0)
            {
                logger?.LogWarning(
                    "DbConfigOptions.AuditReads is true, but IConfigAuditStore is not registered. " +
                    "Read audit rows will not be written. Register IConfigAuditStore in DI " +
                    "(provider packages register an EfCoreConfigAuditStore by default when UseEntityFrameworkCore is called).");
            }

            return;
        }

        var clock = timeProvider ?? TimeProvider.System;

        var auditEntry = new ConfigAuditEntry(
            Id: Guid.NewGuid(),
            AppName: appName,
            Environment: environment,
            TenantId: string.Empty,
            Key: normalizedKey,
            OldValue: null,
            NewValue: null,
            IsSecret: false,
            Action: ConfigAuditAction.Read,
            ModifiedUtc: clock.GetUtcNow(),
            ModifiedBy: httpContext.User?.Identity?.Name);

        Task writeTask;
        try
        {
            writeTask = auditStore.WriteAsync(auditEntry, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Read audit write failed for {Key}", normalizedKey);
            return;
        }

        var fireAndForget = writeTask.ContinueWith(
            t => logger?.LogWarning(t.Exception, "Read audit write failed for {Key}", normalizedKey),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        _ = fireAndForget;
    }
}

/// <summary>
/// Marker type used as the category name for <see cref="ILogger"/> in
/// <see cref="GetEntryEndpoint"/>. Using a dedicated marker avoids a reference to a
/// static class as a generic type argument.
/// </summary>
internal sealed class GetEntryEndpointMarker;
