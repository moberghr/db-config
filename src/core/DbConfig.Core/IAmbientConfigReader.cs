namespace DbConfig.Core;

/// <summary>
/// Convenience read overloads that resolve <c>Scope</c> and <c>Environment</c> from the
/// store's configured <see cref="DbConfigOptions"/>, and the current tenant from the
/// injected <see cref="ITenantResolver"/>. Implemented by the built-in stores
/// (<c>EfCoreConfigStore</c>, <c>InMemoryConfigStore</c>); custom <see cref="IConfigStore"/>
/// implementations that do not maintain ambient state are NOT required to implement this
/// interface — consumers that need ambient reads should depend on this interface directly
/// rather than on <see cref="IConfigStore"/>.
/// </summary>
/// <remarks>
/// Tenant resolution: each call to a non-typed method on this interface invokes
/// <see cref="ITenantResolver.Resolve"/>. The typed variants (<see cref="GetAsync{T}"/>,
/// <see cref="GetForTenantAsync{T}"/>) layer tenant-specific values on top of global
/// defaults using <see cref="TypedSectionPrefix"/>-based binding.
/// </remarks>
public interface IAmbientConfigReader
{
    /// <summary>
    /// Returns all entries for the Scope and Environment configured on the store's
    /// <see cref="DbConfigOptions"/>, scoped to the tenant returned by the injected
    /// <see cref="ITenantResolver"/> (falls back to global / <c>TenantId = ""</c> when
    /// the resolver returns null/empty or no resolver is registered).
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Returns the entry for the given <paramref name="key"/> using the Scope and
    /// Environment configured on the store's <see cref="DbConfigOptions"/>, and the
    /// tenant returned by the injected <see cref="ITenantResolver"/> (falls back to
    /// global / <c>TenantId = ""</c> when the resolver returns null/empty or no resolver
    /// is registered).
    /// </summary>
    Task<ConfigEntryRecord?> GetAsync(string key, CancellationToken ct);

    /// <summary>
    /// Materializes a typed POCO from the configured Scope/Environment for the current
    /// tenant (via <see cref="ITenantResolver"/>) merged on top of global defaults. The
    /// configuration section name is <c>typeof(T).Name</c> verbatim (no suffix stripping,
    /// no convention magic), with the CLR generic-arity suffix removed.
    /// </summary>
    Task<T> GetAsync<T>(CancellationToken ct)
        where T : class, new();

    /// <summary>
    /// Returns all entries for <paramref name="tenantId"/> using the Scope and Environment
    /// configured on the store's <see cref="DbConfigOptions"/>. Does not include global
    /// (<c>TenantId = ""</c>) entries.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(string tenantId, CancellationToken ct);

    /// <summary>
    /// Returns the entry for <paramref name="tenantId"/> and <paramref name="key"/> using
    /// the Scope and Environment configured on the store's <see cref="DbConfigOptions"/>.
    /// No fallback to global — pass <c>tenantId = ""</c> to read a global entry, or use
    /// the typed overload <see cref="GetForTenantAsync{T}"/> for tenant-over-global merge
    /// semantics.
    /// </summary>
    Task<ConfigEntryRecord?> GetForTenantAsync(string tenantId, string key, CancellationToken ct);

    /// <summary>
    /// Materializes a typed POCO for the explicit <paramref name="tenantId"/>, layered on
    /// top of the global (<c>TenantId = ""</c>) defaults. Tenant values override globals on
    /// keys present in both; global values pass through for keys present only in the
    /// global layer.
    /// </summary>
    Task<T> GetForTenantAsync<T>(string tenantId, CancellationToken ct)
        where T : class, new();
}
