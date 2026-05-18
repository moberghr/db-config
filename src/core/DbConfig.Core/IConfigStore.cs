namespace DbConfig.Core;

/// <summary>
/// Abstraction over the backing store for configuration entries.
/// Implemented by provider packages (SQL Server, PostgreSQL) and by
/// <see cref="InMemoryConfigStore"/> for testing.
/// </summary>
public interface IConfigStore
{
    /// <summary>
    /// Returns all global (tenantId = "") entries for the given app and environment.
    /// For tenant-specific entries use <see cref="GetAllForTenantAsync"/> or
    /// <see cref="GetAllForAllTenantsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllAsync(string appName, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the single global (tenantId = "") entry identified by (appName, environment, key),
    /// or <see langword="null"/> if no such entry exists.
    /// Implementations must issue a targeted single-row query — callers rely on this
    /// to avoid a full-scope scan when fetching a single key.
    /// For tenant-specific lookup use <see cref="GetForTenantAsync"/>.
    /// </summary>
    Task<ConfigEntry?> GetAsync(string appName, string environment, string key, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntry.ModifiedUtc"/> value across all global
    /// (tenantId = "") entries for the given app and environment, or <see langword="null"/>
    /// if there are no entries. Used as a cheap change-detection watermark.
    /// For tenant-specific watermark use <see cref="GetLatestModifiedUtcForTenantAsync"/>.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string appName, string environment, CancellationToken ct);

    /// <summary>Inserts or updates an entry. Last-writer-wins on concurrent upserts to the same key.
    /// The <see cref="ConfigEntry.TenantId"/> on the entry determines the tenant scope.</summary>
    Task UpsertAsync(ConfigEntry entry, CancellationToken ct);

    /// <summary>Deletes the global (tenantId = "") entry identified by (appName, environment, key).
    /// No-op if not found. For tenant-specific delete use <see cref="DeleteForTenantAsync"/>.</summary>
    Task DeleteAsync(string appName, string environment, string key, CancellationToken ct);

    /// <summary>
    /// Returns all global (tenantId = "") entries whose (AppName, Environment) matches any
    /// (appName ∈ appNames, environment) pair.
    /// Results are returned in the same order as <paramref name="appNames"/>, so callers can rely on
    /// precedence iteration order (last element wins on duplicate keys).
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllScopedAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntry.ModifiedUtc"/> across all global (tenantId = "")
    /// entries matching any (appName ∈ appNames, environment) pair, or <see langword="null"/> if
    /// there are no such entries. Used as a cheap multi-scope watermark for change detection.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct);

    // -------------------------------------------------------------------------
    // Tenant-aware overloads (B54)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all entries for the given app, environment, and explicit tenant.
    /// Does NOT include global (tenantId = "") entries — use <see cref="GetAllAsync"/> for those.
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllForTenantAsync(
        string appName, string environment, string tenantId, CancellationToken ct);

    /// <summary>
    /// Returns the single entry identified by (appName, environment, tenantId, key),
    /// or <see langword="null"/> if no such entry exists.
    /// No fallback to global — fallback logic belongs in the polling provider layer.
    /// </summary>
    Task<ConfigEntry?> GetForTenantAsync(
        string appName, string environment, string tenantId, string key, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntry.ModifiedUtc"/> across all entries for the
    /// given app, environment, and tenant, or <see langword="null"/> if there are none.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
        string appName, string environment, string tenantId, CancellationToken ct);

    /// <summary>
    /// Deletes the entry identified by (appName, environment, tenantId, key).
    /// No-op if not found.
    /// </summary>
    Task DeleteForTenantAsync(
        string appName, string environment, string tenantId, string key, CancellationToken ct);

    /// <summary>
    /// Returns ALL entries for the given app and environment across every tenant
    /// (including the global default where tenantId = "").
    /// Used by the polling provider at boot/reload to load the full multi-tenant snapshot.
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllForAllTenantsAsync(
        string appName, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntry.ModifiedUtc"/> across ALL entries for the
    /// given app and environment, regardless of tenant (including tenantId = "").
    /// Used by the polling provider as a change-detection watermark that covers every tenant.
    /// Returns <see langword="null"/> if no entries exist.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
        string appName, string environment, CancellationToken ct);

    /// <summary>
    /// Returns ALL entries (every tenant including global tenantId = "") whose AppName
    /// matches any element of <paramref name="appNames"/> in the given environment.
    /// Used by the polling provider at boot/reload to load the full scope × tenant snapshot.
    /// Results MUST be returned in the same order as <paramref name="appNames"/> so callers
    /// can rely on precedence iteration order (last element wins per (tenant, key) tuple).
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllScopedForAllTenantsAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntry.ModifiedUtc"/> across ALL entries
    /// (every tenant) whose AppName ∈ <paramref name="appNames"/> in the given environment.
    /// Used as a multi-scope, multi-tenant watermark for change detection.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct);
}
