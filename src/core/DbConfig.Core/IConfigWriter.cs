namespace DbConfig.Core;

/// <summary>
/// Mutation surface over the backing store. Consumed by HTTP write endpoints and any
/// caller that needs to insert, update, or delete configuration entries.
/// </summary>
public interface IConfigWriter
{
    /// <summary>
    /// Inserts or updates an entry. Last-writer-wins on concurrent upserts to the same
    /// key. The <see cref="ConfigEntryRecord.TenantId"/> on the entry determines the
    /// tenant scope (empty string = global default).
    /// </summary>
    Task UpsertAsync(ConfigEntryRecord entry, CancellationToken ct);

    /// <summary>
    /// Deletes the global (<c>TenantId = ""</c>) entry identified by
    /// <paramref name="scope"/>, <paramref name="environment"/>, and <paramref name="key"/>.
    /// No-op if not found. For tenant-specific delete use <see cref="DeleteForTenantAsync"/>.
    /// </summary>
    Task DeleteAsync(string scope, string environment, string key, CancellationToken ct);

    /// <summary>
    /// Deletes the entry identified by <paramref name="scope"/>, <paramref name="environment"/>,
    /// <paramref name="tenantId"/>, and <paramref name="key"/>. No-op if not found.
    /// </summary>
    Task DeleteForTenantAsync(
        string scope, string environment, string tenantId, string key, CancellationToken ct);
}
