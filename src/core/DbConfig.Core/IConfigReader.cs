namespace DbConfig.Core;

/// <summary>
/// Read-only access to configuration entries with explicit <c>(scope, environment, ...)</c>
/// arguments. Consumed by HTTP read endpoints and any caller that needs to look up entries
/// outside of an ambient request scope.
/// </summary>
/// <remarks>
/// Implementations decrypt secret entries before returning. Reads NEVER fall back across
/// tenants — the tenant-aware variants return null for a missing tenant entry, never the
/// global default. Fallback (tenant → global → null) is the responsibility of higher-level
/// callers such as <see cref="DbConfigConfigurationProvider"/>.
/// </remarks>
public interface IConfigReader
{
    /// <summary>
    /// Returns all global (tenantId = "") entries for the given scope and environment.
    /// For tenant-specific entries use <see cref="GetAllForTenantAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(string scope, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the single global (tenantId = "") entry identified by
    /// <paramref name="scope"/>, <paramref name="environment"/>, and <paramref name="key"/>,
    /// or <see langword="null"/> if no such entry exists. Implementations must issue a
    /// targeted single-row query — callers rely on this to avoid a full-scope scan when
    /// fetching a single key.
    /// </summary>
    Task<ConfigEntryRecord?> GetAsync(string scope, string environment, string key, CancellationToken ct);

    /// <summary>
    /// Returns all global (tenantId = "") entries whose <c>(Scope, Environment)</c> matches any
    /// <c>(scope ∈ scopes, environment)</c> pair. Results are returned in the same order as
    /// <paramref name="scopes"/> so callers can rely on precedence iteration order (last
    /// element wins on duplicate keys).
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct);

    /// <summary>
    /// Returns all entries for the given scope, environment, and explicit tenant.
    /// Does NOT include global (tenantId = "") entries — use <see cref="GetAllAsync"/> for those.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(
        string scope, string environment, string tenantId, CancellationToken ct);

    /// <summary>
    /// Returns the single entry identified by
    /// <paramref name="scope"/>, <paramref name="environment"/>, <paramref name="tenantId"/>,
    /// and <paramref name="key"/>, or <see langword="null"/> if no such entry exists.
    /// No fallback to global — fallback logic belongs in the polling provider layer.
    /// </summary>
    Task<ConfigEntryRecord?> GetForTenantAsync(
        string scope, string environment, string tenantId, string key, CancellationToken ct);
}
