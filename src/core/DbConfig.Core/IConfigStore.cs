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
    /// For tenant-specific entries use the tenant-aware overloads or
    /// <see cref="GetAllForAllTenantsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllAsync(string appName, string environment, CancellationToken ct);

    /// <summary>
    /// Returns all entries for the AppName and Environment configured on the store's
    /// <see cref="DbConfigOptions"/>, scoped to the tenant id returned by the injected
    /// <see cref="ITenantResolver"/> (falls back to global / tenantId = "" when the resolver
    /// returns null/empty or no resolver is registered). Convenience overload (v0.11.1).
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllAsync(CancellationToken ct)
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support the implicit-app/env GetAllAsync overload. " +
            "Use GetAllAsync(appName, environment, ct) instead.");

    /// <summary>
    /// Returns the single global (tenantId = "") entry identified by (appName, environment, key),
    /// or <see langword="null"/> if no such entry exists.
    /// Implementations must issue a targeted single-row query — callers rely on this
    /// to avoid a full-scope scan when fetching a single key.
    /// </summary>
    Task<ConfigEntry?> GetAsync(string appName, string environment, string key, CancellationToken ct);

    /// <summary>
    /// Returns the entry for the given <paramref name="key"/> using the AppName and Environment
    /// configured on the store's <see cref="DbConfigOptions"/>, and the tenant id returned by the
    /// injected <see cref="ITenantResolver"/> (falls back to global / tenantId = "" when the
    /// resolver returns null/empty or no resolver is registered). Convenience overload (v0.11.1).
    /// </summary>
    /// <remarks>
    /// Built-in store implementations expose this overload. Custom <see cref="IConfigStore"/>
    /// implementations that do not maintain ambient AppName/Environment state throw
    /// <see cref="NotSupportedException"/>; callers must then use the explicit-app/env overload.
    /// </remarks>
    Task<ConfigEntry?> GetAsync(string key, CancellationToken ct)
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support the implicit-app/env GetAsync overload. " +
            "Use GetAsync(appName, environment, key, ct) instead.");

    /// <summary>
    /// Materializes a typed POCO from the configured AppName/Environment for the current tenant
    /// (via <see cref="ITenantResolver"/>) merged on top of global defaults. The configuration
    /// section name is <c>typeof(T).Name</c> verbatim — no suffix stripping, no convention magic.
    /// </summary>
    /// <remarks>
    /// Example: for <c>StripeOptions</c>, entries with keys prefixed by <c>"StripeOptions:"</c>
    /// are bound (e.g. <c>"StripeOptions:ApiKey"</c> → <c>ApiKey</c>). Tenant overrides win on
    /// keys present in both layers; keys present only in the global layer pass through.
    /// </remarks>
    Task<T> GetAsync<T>(CancellationToken ct)
        where T : class, new()
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support typed GetAsync<T>(). " +
            "Use GetAllAsync(appName, environment, ct) and bind manually instead.");

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
    /// No-op if not found. For tenant-specific delete use <c>DeleteForTenantAsync</c>.</summary>
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
    /// Does NOT include global (tenantId = "") entries — use <c>GetAllAsync</c> for those.
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllForTenantAsync(
        string appName, string environment, string tenantId, CancellationToken ct);

    /// <summary>
    /// Returns all entries for <paramref name="tenantId"/> using the AppName and Environment
    /// configured on the store's <see cref="DbConfigOptions"/>. Does not include global
    /// (tenantId = "") entries. Convenience overload (v0.11.1).
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllForTenantAsync(string tenantId, CancellationToken ct)
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support the implicit-app/env GetAllForTenantAsync overload. " +
            "Use GetAllForTenantAsync(appName, environment, tenantId, ct) instead.");

    /// <summary>
    /// Returns the single entry identified by (appName, environment, tenantId, key),
    /// or <see langword="null"/> if no such entry exists.
    /// No fallback to global — fallback logic belongs in the polling provider layer.
    /// </summary>
    Task<ConfigEntry?> GetForTenantAsync(
        string appName, string environment, string tenantId, string key, CancellationToken ct);

    /// <summary>
    /// Returns the entry for (<paramref name="tenantId"/>, <paramref name="key"/>) using the
    /// AppName and Environment configured on the store's <see cref="DbConfigOptions"/>. No
    /// fallback to global — pass <c>tenantId = ""</c> to read a global entry, or use the typed
    /// overload <c>GetForTenantAsync&lt;T&gt;</c> for tenant-over-global merge semantics.
    /// Convenience overload (v0.11.1).
    /// </summary>
    Task<ConfigEntry?> GetForTenantAsync(string tenantId, string key, CancellationToken ct)
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support the implicit-app/env GetForTenantAsync overload. " +
            "Use GetForTenantAsync(appName, environment, tenantId, key, ct) instead.");

    /// <summary>
    /// Materializes a typed POCO for the explicit <paramref name="tenantId"/>, layered on top of
    /// the global (tenantId = "") defaults. Section name is <c>typeof(T).Name</c> verbatim. Tenant
    /// values override globals on keys present in both; global values pass through for keys
    /// present only in the global layer. Convenience overload (v0.11.1).
    /// </summary>
    Task<T> GetForTenantAsync<T>(string tenantId, CancellationToken ct)
        where T : class, new()
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support typed GetForTenantAsync<T>(). " +
            "Use GetAllForTenantAsync(appName, environment, tenantId, ct) and bind manually instead.");

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

    /// <summary>
    /// Flat scan with optional filters. Each non-<see langword="null"/> parameter narrows the
    /// result set (AND semantics). Used by the admin UI to show a global "all entries" view
    /// on first paint without requiring AppName + Environment input.
    /// </summary>
    /// <param name="appName">Case-insensitive equality on <see cref="ConfigEntry.AppName"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="environment">Case-insensitive equality on <see cref="ConfigEntry.Environment"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="tenantId">Case-sensitive equality on <see cref="ConfigEntry.TenantId"/>, or <see langword="null"/> for no filter. Empty string matches global-default entries.</param>
    /// <param name="keyPrefix">Case-insensitive starts-with match on <see cref="ConfigEntry.Key"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="take">Maximum number of rows to return. Implementations apply this as a SQL <c>TOP</c> / <c>LIMIT</c> — never as in-memory truncation of a full scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Entries ordered by (AppName, Environment, TenantId, Key) ascending for deterministic
    /// pagination. Secret entries are returned in plaintext after decryption.
    /// </returns>
    Task<IReadOnlyList<ConfigEntry>> QueryAsync(
        string? appName,
        string? environment,
        string? tenantId,
        string? keyPrefix,
        int take,
        CancellationToken ct);
}
