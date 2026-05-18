using System.Text.Json;
using System.Threading;
using DbConfig.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DbConfig.Http.Endpoints;

internal static class ListEntriesEndpoint
{
    private static int _warnedAboutMissingStore;

    internal static async Task HandleAsync(
        string appName,
        string environment,
        IConfigStore store,
        HttpContext httpContext,
        [FromServices] IConfigAuditStore? auditStore,
        [FromServices] DbConfigOptions? options,
        [FromServices] ILogger<ListEntriesEndpointMarker>? logger,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken ct)
    {
        var includeScopesRaw = httpContext.Request.Query["includeScopes"].FirstOrDefault();
        var tenantIdRaw = httpContext.Request.Query["tenantId"].FirstOrDefault();
        var tenantId = string.IsNullOrEmpty(tenantIdRaw) ? string.Empty : tenantIdRaw;
        var allTenantsRaw = httpContext.Request.Query["allTenants"].FirstOrDefault();
        var allTenants = string.Equals(allTenantsRaw, "true", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<ConfigEntry> entries;

        if (allTenants)
        {
            // Admin view: all entries across all tenants (ignores tenantId and includeScopes).
            entries = await store.GetAllForAllTenantsAsync(appName, environment, ct);
        }
        else if (!string.IsNullOrEmpty(tenantId))
        {
            // Single tenant view.
            entries = await store.GetAllForTenantAsync(appName, environment, tenantId, ct);
        }
        else if (!string.IsNullOrEmpty(includeScopesRaw))
        {
            // Parse comma-separated scopes: trim, drop empties, deduplicate preserving order,
            // then append the path's appName last (highest precedence).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scopes = new List<string>();

            foreach (var part in includeScopesRaw.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (seen.Add(trimmed))
                {
                    scopes.Add(trimmed);
                }
            }

            // Remove the path appName from earlier positions so it can be appended last.
            var pathAppIndex = scopes.FindIndex(s => string.Equals(s, appName, StringComparison.OrdinalIgnoreCase));
            if (pathAppIndex >= 0)
            {
                scopes.RemoveAt(pathAppIndex);
            }

            scopes.Add(appName);

            entries = await store.GetAllScopedAsync(scopes, environment, ct);
        }
        else
        {
            // Legacy: global entries only.
            entries = await store.GetAllAsync(appName, environment, ct);
        }

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, entries, JsonOptions.Default, ct);

        WriteReadAudit(appName, environment, httpContext, auditStore, options, logger, timeProvider);
    }

    private static void WriteReadAudit(
        string appName,
        string environment,
        HttpContext httpContext,
        IConfigAuditStore? auditStore,
        DbConfigOptions? options,
        ILogger<ListEntriesEndpointMarker>? logger,
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
            Key: "*",
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
            logger?.LogWarning(ex, "Read audit write failed for list request on {AppName}/{Environment}", appName, environment);
            return;
        }

        var fireAndForget = writeTask.ContinueWith(
            t => logger?.LogWarning(t.Exception, "Read audit write failed for list request on {AppName}/{Environment}", appName, environment),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        _ = fireAndForget;
    }
}

/// <summary>
/// Marker type used as the category name for <see cref="ILogger"/> in
/// <see cref="ListEntriesEndpoint"/>. Using a dedicated marker avoids a reference to a
/// static class as a generic type argument.
/// </summary>
internal sealed class ListEntriesEndpointMarker;
