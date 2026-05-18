using DbConfig.Core;

namespace PaymentsApi;

// Reads the current tenant id from the X-Tenant-Id header.
// Real hosts extract tenant identity from JWT claims, route values, subdomains, etc.
internal sealed class HeaderTenantResolver(IHttpContextAccessor httpContext) : ITenantResolver
{
    public string? Resolve()
    {
        var ctx = httpContext.HttpContext;
        if (ctx is null)
        {
            return null;
        }

        return ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var v) ? v.ToString() : null;
    }
}
