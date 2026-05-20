using DbConfig.Core;
using Microsoft.AspNetCore.Http;

namespace DbConfig.Http.Endpoints;

internal static class DeleteEntryEndpoint
{
    internal static async Task<IResult> HandleAsync(
        string scope,
        string environment,
        string key,
        IConfigStore store,
        IDbConfigReloadSignal reloadSignal,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var normalizedKey = key.Replace('/', ':');
        var tenantIdRaw = httpContext.Request.Query["tenantId"].FirstOrDefault();
        var tenantId = string.IsNullOrEmpty(tenantIdRaw) ? string.Empty : tenantIdRaw;

        if (string.IsNullOrEmpty(tenantId))
        {
            await store.DeleteAsync(scope, environment, normalizedKey, ct);
        }
        else
        {
            await store.DeleteForTenantAsync(scope, environment, tenantId, normalizedKey, ct);
        }

        reloadSignal.Trigger();

        return Results.NoContent();
    }
}
