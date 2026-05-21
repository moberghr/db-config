namespace DbConfig.Core;

/// <summary>
/// Access to the configuration audit log.
/// </summary>
/// <remarks>
/// Mutation audit writes are performed internally by the config store implementation (e.g.
/// <c>EfCoreConfigStore</c>) in the same <c>SaveChangesAsync</c> as the mutation.
/// Read audit writes use <see cref="WriteAsync"/> which is out-of-transaction (fire-and-forget).
/// <para>
/// When an entry was stored with <c>IsSecret=true</c>, the returned <see cref="ConfigAuditEntryRecord"/>
/// values (<see cref="ConfigAuditEntryRecord.OldValue"/> and <see cref="ConfigAuditEntryRecord.NewValue"/>)
/// are decrypted plaintext — the store handles decryption internally before mapping.
/// </para>
/// </remarks>
public interface IConfigAuditStore
{
    /// <summary>
    /// Returns the most recent audit entries for the global (tenantId = "") key,
    /// ordered most-recent-first.
    /// For tenant-specific history use <see cref="GetHistoryForTenantAsync"/>.
    /// </summary>
    /// <param name="scope">The scope (logical application name).</param>
    /// <param name="environment">The environment name scope.</param>
    /// <param name="key">The configuration key.</param>
    /// <param name="take">Maximum number of entries to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only list of <see cref="ConfigAuditEntryRecord"/> records ordered by
    /// <see cref="ConfigAuditEntryRecord.ModifiedUtc"/> descending. Returns an empty list when the
    /// key has no audit history.
    /// </returns>
    Task<IReadOnlyList<ConfigAuditEntryRecord>> GetHistoryAsync(
        string scope, string environment, string key, int take, CancellationToken ct);

    /// <summary>
    /// Returns the most recent audit entries for the given tenant-specific key,
    /// ordered most-recent-first.
    /// </summary>
    /// <param name="scope">The scope (logical application name).</param>
    /// <param name="environment">The environment name scope.</param>
    /// <param name="tenantId">The tenant identifier. Pass empty string for global (default) entries.</param>
    /// <param name="key">The configuration key.</param>
    /// <param name="take">Maximum number of entries to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ConfigAuditEntryRecord>> GetHistoryForTenantAsync(
        string scope, string environment, string tenantId, string key, int take, CancellationToken ct);

    /// <summary>
    /// Writes an audit row out-of-transaction. Used by HTTP read-audit logic.
    /// Mutations use the in-transaction path (EfCoreConfigStore writes via its DbContext).
    /// The <see cref="ConfigAuditEntryRecord.TenantId"/> on the entry is stored verbatim.
    /// </summary>
    Task WriteAsync(ConfigAuditEntryRecord entry, CancellationToken ct);

    /// <summary>
    /// Returns audit entries matching the supplied filters, ordered most-recent-first
    /// (with <see cref="ConfigAuditEntryRecord.Key"/> ascending as a stable secondary sort).
    /// All filter parameters are optional — pass <see langword="null"/> for "no filter".
    /// Returned values are plaintext when <see cref="ConfigAuditEntryRecord.IsSecret"/> is
    /// <see langword="true"/>; the store handles decryption internally.
    /// </summary>
    /// <param name="scope">Exact-match <see cref="ConfigAuditEntryRecord.Scope"/> filter, or <see langword="null"/>.</param>
    /// <param name="environment">Exact-match <see cref="ConfigAuditEntryRecord.Environment"/> filter, or <see langword="null"/>.</param>
    /// <param name="tenantId">Exact-match <see cref="ConfigAuditEntryRecord.TenantId"/> filter (empty string = global), or <see langword="null"/> for no filter.</param>
    /// <param name="keyPrefix">Case-insensitive starts-with filter on <see cref="ConfigAuditEntryRecord.Key"/>, or <see langword="null"/>.</param>
    /// <param name="action">Exact-match <see cref="ConfigAuditAction"/> filter, or <see langword="null"/>.</param>
    /// <param name="take">Maximum number of entries to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ConfigAuditEntryRecord>> QueryAsync(
        string? scope,
        string? environment,
        string? tenantId,
        string? keyPrefix,
        ConfigAuditAction? action,
        int take,
        CancellationToken ct);
}
