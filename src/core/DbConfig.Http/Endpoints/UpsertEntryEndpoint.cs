using DbConfig.Core;
using Microsoft.AspNetCore.Http;

namespace DbConfig.Http.Endpoints;

internal static class UpsertEntryEndpoint
{
    internal static async Task<IResult> HandleAsync(
        string appName,
        string environment,
        string key,
        UpsertEntryRequest body,
        IConfigStore store,
        IDbConfigReloadSignal reloadSignal,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var normalizedKey = key.Replace('/', ':');
        var modifiedBy = httpContext.User?.Identity?.Name;
        var now = timeProvider.GetUtcNow();

        var tenantId = string.IsNullOrEmpty(body.TenantId) ? string.Empty : body.TenantId;

        var entry = new ConfigEntry(
            appName,
            environment,
            tenantId,
            normalizedKey,
            body.Value,
            body.IsSecret,
            now,
            modifiedBy);

        await store.UpsertAsync(entry, ct);
        reloadSignal.Trigger();

        return Results.NoContent();
    }
}
