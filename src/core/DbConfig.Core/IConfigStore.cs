namespace DbConfig.Core;

/// <summary>
/// Abstraction over the backing store for configuration entries.
/// Implemented by provider packages (SQL Server, PostgreSQL) and by
/// <see cref="InMemoryConfigStore"/> for testing.
/// </summary>
public interface IConfigStore
{
    /// <summary>
    /// Returns all global (tenantId = "") entries for the given scope and environment.
    /// For tenant-specific entries use the tenant-aware overloads or
    /// <see cref="GetAllForAllTenantsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(string scope, string environment, CancellationToken ct);

    /// <summary>
    /// Returns all entries for the Scope and Environment configured on the store's
    /// <see cref="DbConfigOptions"/>, scoped to the tenant id returned by the injected
    /// <see cref="ITenantResolver"/> (falls back to global / tenantId = "" when the resolver
    /// returns null/empty or no resolver is registered). Convenience overload.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(CancellationToken ct)
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support the implicit-scope/env GetAllAsync overload. " +
            "Use GetAllAsync(scope, environment, ct) instead.");

    /// <summary>
    /// Returns the single global (tenantId = "") entry identified by (scope, environment, key),
    /// or <see langword="null"/> if no such entry exists.
    /// Implementations must issue a targeted single-row query — callers rely on this
    /// to avoid a full-scope scan when fetching a single key.
    /// </summary>
    Task<ConfigEntryRecord?> GetAsync(string scope, string environment, string key, CancellationToken ct);

    /// <summary>
    /// Returns the entry for the given <paramref name="key"/> using the Scope and Environment
    /// configured on the store's <see cref="DbConfigOptions"/>, and the tenant id returned by the
    /// injected <see cref="ITenantResolver"/> (falls back to global / tenantId = "" when the
    /// resolver returns null/empty or no resolver is registered). Convenience overload.
    /// </summary>
    /// <remarks>
    /// Built-in store implementations expose this overload. Custom <see cref="IConfigStore"/>
    /// implementations that do not maintain ambient Scope/Environment state throw
    /// <see cref="NotSupportedException"/>; callers must then use the explicit-scope/env overload.
    /// </remarks>
    Task<ConfigEntryRecord?> GetAsync(string key, CancellationToken ct)
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support the implicit-scope/env GetAsync overload. " +
            "Use GetAsync(scope, environment, key, ct) instead.");

    /// <summary>
    /// Materializes a typed POCO from the configured Scope/Environment for the current tenant
    /// (via <see cref="ITenantResolver"/>) merged on top of global defaults. The configuration
    /// section name is <c>typeof(T).Name</c> verbatim (no suffix stripping, no convention magic),
    /// with the CLR generic-arity suffix removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Example: for <c>StripeOptions</c>, entries with keys prefixed by <c>"StripeOptions:"</c>
    /// are bound (e.g. <c>"StripeOptions:ApiKey"</c> → <c>ApiKey</c>). Tenant overrides win on
    /// keys present in both layers; keys present only in the global layer pass through.
    /// </para>
    /// <para>
    /// Returns <typeparamref name="T"/> with default POCO values if no DB entries match the
    /// section prefix — matches ASP.NET Core's <c>IConfiguration.Bind()</c> semantics. Callers
    /// requiring at-least-one-key semantics should check the result or inspect via
    /// <see cref="QueryAsync"/> beforehand.
    /// </para>
    /// <para>
    /// Generic type arity suffix is stripped: <c>MyOptions&lt;TKind&gt;</c> binds from the
    /// <c>"MyOptions:"</c> prefix. If you have multiple generic instantiations and want
    /// separate sections, define a non-generic outer type that wraps the generic and bind that.
    /// </para>
    /// </remarks>
    Task<T> GetAsync<T>(CancellationToken ct)
        where T : class, new()
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support typed GetAsync<T>(). " +
            "Use GetAllAsync(scope, environment, ct) and bind manually instead.");

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> value across all global
    /// (tenantId = "") entries for the given scope and environment, or <see langword="null"/>
    /// if there are no entries. Used as a cheap change-detection watermark.
    /// For tenant-specific watermark use <see cref="GetLatestModifiedUtcForTenantAsync"/>.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string scope, string environment, CancellationToken ct);

    /// <summary>Inserts or updates an entry. Last-writer-wins on concurrent upserts to the same key.
    /// The <see cref="ConfigEntryRecord.TenantId"/> on the entry determines the tenant scope.</summary>
    Task UpsertAsync(ConfigEntryRecord entry, CancellationToken ct);

    /// <summary>Deletes the global (tenantId = "") entry identified by (scope, environment, key).
    /// No-op if not found. For tenant-specific delete use <c>DeleteForTenantAsync</c>.</summary>
    Task DeleteAsync(string scope, string environment, string key, CancellationToken ct);

    /// <summary>
    /// Returns all global (tenantId = "") entries whose (Scope, Environment) matches any
    /// (scope ∈ scopes, environment) pair.
    /// Results are returned in the same order as <paramref name="scopes"/>, so callers can rely on
    /// precedence iteration order (last element wins on duplicate keys).
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> across all global (tenantId = "")
    /// entries matching any (scope ∈ scopes, environment) pair, or <see langword="null"/> if
    /// there are no such entries. Used as a cheap multi-scope watermark for change detection.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct);

    // -------------------------------------------------------------------------
    // Tenant-aware overloads
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all entries for the given scope, environment, and explicit tenant.
    /// Does NOT include global (tenantId = "") entries — use <c>GetAllAsync</c> for those.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(
        string scope, string environment, string tenantId, CancellationToken ct);

    /// <summary>
    /// Returns all entries for <paramref name="tenantId"/> using the Scope and Environment
    /// configured on the store's <see cref="DbConfigOptions"/>. Does not include global
    /// (tenantId = "") entries. Convenience overload.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(string tenantId, CancellationToken ct)
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support the implicit-scope/env GetAllForTenantAsync overload. " +
            "Use GetAllForTenantAsync(scope, environment, tenantId, ct) instead.");

    /// <summary>
    /// Returns the single entry identified by (scope, environment, tenantId, key),
    /// or <see langword="null"/> if no such entry exists.
    /// No fallback to global — fallback logic belongs in the polling provider layer.
    /// </summary>
    Task<ConfigEntryRecord?> GetForTenantAsync(
        string scope, string environment, string tenantId, string key, CancellationToken ct);

    /// <summary>
    /// Returns the entry for (<paramref name="tenantId"/>, <paramref name="key"/>) using the
    /// Scope and Environment configured on the store's <see cref="DbConfigOptions"/>. No
    /// fallback to global — pass <c>tenantId = ""</c> to read a global entry, or use the typed
    /// overload <c>GetForTenantAsync&lt;T&gt;</c> for tenant-over-global merge semantics.
    /// Convenience overload.
    /// </summary>
    Task<ConfigEntryRecord?> GetForTenantAsync(string tenantId, string key, CancellationToken ct)
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support the implicit-scope/env GetForTenantAsync overload. " +
            "Use GetForTenantAsync(scope, environment, tenantId, key, ct) instead.");

    /// <summary>
    /// Materializes a typed POCO for the explicit <paramref name="tenantId"/>, layered on top of
    /// the global (tenantId = "") defaults. Section name is <c>typeof(T).Name</c> verbatim (with
    /// the CLR generic-arity suffix stripped). Tenant values override globals on keys present in
    /// both; global values pass through for keys present only in the global layer. Convenience
    /// overload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <typeparamref name="T"/> with default POCO values if no DB entries match the
    /// section prefix — matches ASP.NET Core's <c>IConfiguration.Bind()</c> semantics. Callers
    /// requiring at-least-one-key semantics should check the result or inspect via
    /// <see cref="QueryAsync"/> beforehand.
    /// </para>
    /// <para>
    /// Generic type arity suffix is stripped: <c>MyOptions&lt;TKind&gt;</c> binds from the
    /// <c>"MyOptions:"</c> prefix. Multiple generic instantiations therefore collide on the
    /// same section — define a non-generic wrapper type for separate sections.
    /// </para>
    /// </remarks>
    Task<T> GetForTenantAsync<T>(string tenantId, CancellationToken ct)
        where T : class, new()
        => throw new NotSupportedException(
            "This IConfigStore implementation does not support typed GetForTenantAsync<T>(). " +
            "Use GetAllForTenantAsync(scope, environment, tenantId, ct) and bind manually instead.");

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> across all entries for the
    /// given scope, environment, and tenant, or <see langword="null"/> if there are none.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
        string scope, string environment, string tenantId, CancellationToken ct);

    /// <summary>
    /// Deletes the entry identified by (scope, environment, tenantId, key).
    /// No-op if not found.
    /// </summary>
    Task DeleteForTenantAsync(
        string scope, string environment, string tenantId, string key, CancellationToken ct);

    /// <summary>
    /// Returns ALL entries for the given scope and environment across every tenant
    /// (including the global default where tenantId = "").
    /// Used by the polling provider at boot/reload to load the full multi-tenant snapshot.
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllForAllTenantsAsync(
        string scope, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> across ALL entries for the
    /// given scope and environment, regardless of tenant (including tenantId = "").
    /// Used by the polling provider as a change-detection watermark that covers every tenant.
    /// Returns <see langword="null"/> if no entries exist.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
        string scope, string environment, CancellationToken ct);

    /// <summary>
    /// Returns ALL entries (every tenant including global tenantId = "") whose Scope
    /// matches any element of <paramref name="scopes"/> in the given environment.
    /// Used by the polling provider at boot/reload to load the full scope × tenant snapshot.
    /// Results MUST be returned in the same order as <paramref name="scopes"/> so callers
    /// can rely on precedence iteration order (last element wins per (tenant, key) tuple).
    /// </summary>
    Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedForAllTenantsAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> across ALL entries
    /// (every tenant) whose Scope ∈ <paramref name="scopes"/> in the given environment.
    /// Used as a multi-scope, multi-tenant watermark for change detection.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct);

    /// <summary>
    /// Flat scan with optional filters. Each non-<see langword="null"/> parameter narrows the
    /// result set (AND semantics). Used by the admin UI to show a global "all entries" view
    /// on first paint without requiring Scope + Environment input.
    /// </summary>
    /// <param name="scope">Case-insensitive equality on <see cref="ConfigEntryRecord.Scope"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="environment">Case-insensitive equality on <see cref="ConfigEntryRecord.Environment"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="tenantId">Case-sensitive equality on <see cref="ConfigEntryRecord.TenantId"/>, or <see langword="null"/> for no filter. Empty string matches global-default entries.</param>
    /// <param name="keyPrefix">Case-insensitive starts-with match on <see cref="ConfigEntryRecord.Key"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="take">Maximum number of rows to return. Implementations apply this as a SQL <c>TOP</c> / <c>LIMIT</c> — never as in-memory truncation of a full scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Entries ordered by (Scope, Environment, TenantId, Key) ascending for deterministic
    /// pagination. Secret entries are returned in plaintext after decryption.
    /// </returns>
    Task<IReadOnlyList<ConfigEntryRecord>> QueryAsync(
        string? scope,
        string? environment,
        string? tenantId,
        string? keyPrefix,
        int take,
        CancellationToken ct);
}
