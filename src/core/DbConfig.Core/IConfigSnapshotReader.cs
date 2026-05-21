namespace DbConfig.Core;

/// <summary>
/// Bulk multi-tenant reads used by the polling provider at boot and on each reload to
/// load the full <c>(Scope × Tenant)</c> snapshot in one query.
/// </summary>
/// <remarks>
/// These methods exist so the polling provider can fetch every tenant's entries in a
/// single call rather than iterating tenants on the read path. Most application code
/// should NOT depend on this interface — use <see cref="IConfigReader"/> for explicit
/// (scope, tenant)-scoped reads, or <see cref="IAmbientConfigReader"/> for current-tenant
/// convenience reads.
/// </remarks>
public interface IConfigSnapshotReader
{
    /// <summary>
    /// Returns ALL entries for the given scope and environment across every tenant
    /// (including the global default where <c>TenantId = ""</c>). Used by the polling
    /// provider at boot/reload to load the full multi-tenant snapshot.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllForAllTenantsAsync(
        string scope, string environment, CancellationToken ct);

    /// <summary>
    /// Returns ALL entries (every tenant including global <c>TenantId = ""</c>) whose Scope
    /// matches any element of <paramref name="scopes"/> in the given environment. Results
    /// MUST be returned in the same order as <paramref name="scopes"/> so callers can rely
    /// on precedence iteration order (last element wins per <c>(tenant, key)</c> tuple).
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedForAllTenantsAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct);
}
