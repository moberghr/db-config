namespace DbConfig.Core;

/// <summary>
/// Resolves typed configuration objects for a specific tenant, bypassing the ambient
/// <see cref="ITenantResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use this when you need to read a tenant's configuration outside of that tenant's
/// request context — e.g. an admin endpoint that compares Stripe settings across tenants,
/// a scheduled job that processes one tenant at a time, or a cross-tenant migration tool.
/// </para>
/// <para>
/// The reader leverages the standard <c>IOptionsSnapshot&lt;T&gt;</c> binding pipeline:
/// every <c>services.Configure&lt;T&gt;(configuration.GetSection("Stripe"))</c> registration
/// (including <c>PostConfigure</c>, code-based configurators, and custom section paths)
/// runs exactly as it would for a normal request. The only difference is the tenant context
/// is locked to the supplied tenant id rather than the resolver's current value.
/// </para>
/// <para>
/// Non-db-config configuration sources (<c>appsettings.json</c>, environment variables, etc.)
/// pass through unchanged — they are tenant-unaware by definition. Only the db-config
/// provider's lookup is scoped to the supplied tenant.
/// </para>
/// <para>
/// Implementation notes: the reader uses a <c>System.Threading.AsyncLocal&lt;T&gt;</c> override
/// on the db-config provider so the tenant id is visible only inside the call. Concurrent
/// calls on different async flows do not interfere with each other and do not leak the
/// override to the host's ambient <c>IConfiguration</c>.
/// </para>
/// </remarks>
public interface ITenantConfigReader
{
    /// <summary>
    /// Binds <typeparamref name="T"/> using the configuration section the consumer registered
    /// via <c>services.Configure&lt;T&gt;(...)</c>, scoped to the supplied
    /// <paramref name="tenantId"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The options type. Must be a reference type with a public parameterless constructor
    /// (the same constraint <c>IOptionsSnapshot&lt;T&gt;</c> imposes at resolution time).
    /// </typeparam>
    /// <param name="tenantId">
    /// The tenant id to resolve config for. Use the empty string for the global
    /// (TenantId = "") entries. May not be null.
    /// </param>
    /// <returns>
    /// A fully-bound <typeparamref name="T"/> identical to what
    /// <c>IOptionsSnapshot&lt;T&gt;.Value</c> would yield inside a request whose
    /// <see cref="ITenantResolver"/> returns <paramref name="tenantId"/>.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown if <paramref name="tenantId"/> is null.
    /// </exception>
    T GetForTenant<T>(string tenantId)
        where T : class;
}
