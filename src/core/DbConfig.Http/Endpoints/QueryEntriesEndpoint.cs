using System.Text.Json;
using System.Threading;
using DbConfig.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DbConfig.Http.Endpoints;

/// <summary>
/// Flat-query endpoint mounted at the root of the entries group.
/// Returns all entries with optional query-string filters so the admin UI can show
/// data on first paint without requiring AppName + Environment input.
/// </summary>
internal static class QueryEntriesEndpoint
{
    private const int DefaultTake = 1000;
    private const int MaxTake = 10000;
    private const int MinTake = 1;

    private static int _warnedAboutMissingStore;

    internal static async Task HandleAsync(
        HttpContext httpContext,
        [FromQuery] string? appName,
        [FromQuery] string? environment,
        [FromQuery] string? tenantId,
        [FromQuery] string? keyPrefix,
        [FromQuery] int? take,
        IConfigStore store,
        string? scopeFilter,
        [FromServices] IConfigAuditStore? auditStore,
        [FromServices] DbConfigOptions? dbOptions,
        [FromServices] ILogger<QueryEntriesEndpointMarker>? logger,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken ct)
    {
        // Scope filter enforcement: when configured, the caller MUST scope to that AppName.
        // Any non-matching appName query (including null/absent) is normalized so cross-scope
        // reads cannot leak via this endpoint.
        var effectiveAppName = appName;
        if (scopeFilter is not null)
        {
            if (string.IsNullOrEmpty(effectiveAppName))
            {
                // No appName supplied — force the scope filter.
                effectiveAppName = scopeFilter;
            }
            else if (!string.Equals(effectiveAppName, scopeFilter, StringComparison.Ordinal))
            {
                // Mismatch — deny rather than silently substitute. Mirrors the path-based
                // endpoints' 403 behavior in EndpointRouteBuilderExtensions.
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
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

        // Empty strings on the query string are treated as "no filter" — matches the
        // UI's behavior where blank input fields mean "show everything".
        var normalizedAppName = string.IsNullOrEmpty(effectiveAppName) ? null : effectiveAppName;
        var normalizedEnvironment = string.IsNullOrEmpty(environment) ? null : environment;
        var normalizedTenantId = tenantId; // empty string is valid (global-default sentinel)
        var normalizedKeyPrefix = string.IsNullOrEmpty(keyPrefix) ? null : keyPrefix;

        var entries = await store.QueryAsync(
            normalizedAppName,
            normalizedEnvironment,
            normalizedTenantId,
            normalizedKeyPrefix,
            effectiveTake,
            ct);

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, entries, JsonOptions.Default, ct);

        WriteReadAudit(normalizedAppName, normalizedEnvironment, httpContext, auditStore, dbOptions, logger, timeProvider);
    }

    private static void WriteReadAudit(
        string? appName,
        string? environment,
        HttpContext httpContext,
        IConfigAuditStore? auditStore,
        DbConfigOptions? options,
        ILogger<QueryEntriesEndpointMarker>? logger,
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

        // Key sentinel "*" — flat query reads multiple keys; "*" is reserved by route normalization
        // so no real config key can collide.
        var auditEntry = new ConfigAuditEntry(
            Id: Guid.NewGuid(),
            AppName: appName ?? string.Empty,
            Environment: environment ?? string.Empty,
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
            logger?.LogWarning(ex, "Read audit write failed for flat query request");
            return;
        }

        var fireAndForget = writeTask.ContinueWith(
            t => logger?.LogWarning(t.Exception, "Read audit write failed for flat query request"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        _ = fireAndForget;
    }
}

/// <summary>
/// Marker type used as the category name for <see cref="ILogger"/> in
/// <see cref="QueryEntriesEndpoint"/>. Using a dedicated marker avoids a reference to a
/// static class as a generic type argument.
/// </summary>
internal sealed class QueryEntriesEndpointMarker;
