namespace DbConfig.Core;

/// <summary>
/// Consumer-implemented resolver that returns the current tenant id for the
/// caller's context. Returning null (or empty string) means "no tenant context";
/// the configuration system falls back to global default entries.
/// </summary>
/// <remarks>
/// Typical implementations read from <c>IHttpContextAccessor</c> and extract the
/// tenant id from a JWT claim, request header, route value, or subdomain.
/// db-config does not ship a resolver — the host decides where tenant identity
/// comes from. Register the resolver via
/// <c>b.AddTenantResolver&lt;MyTenantResolver&gt;()</c> inside the
/// <c>AddDbConfig</c> options block.
/// </remarks>
public interface ITenantResolver
{
    /// <summary>Returns the current tenant id or null if no tenant is in context.</summary>
    string? Resolve();
}
