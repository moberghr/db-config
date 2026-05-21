namespace DbConfig.Core;

/// <summary>
/// Flat-scan query surface used by the admin UI to render a global "all entries" view
/// without requiring Scope + Environment input. Distinct from <see cref="IConfigReader"/>
/// because the filtering shape (every field optional, AND semantics, paginated) does not
/// match the targeted-key reads served by that interface.
/// </summary>
public interface IConfigQuery
{
    /// <summary>
    /// Flat scan with optional filters. Each non-<see langword="null"/> parameter narrows
    /// the result set (AND semantics).
    /// </summary>
    /// <param name="scope">Case-insensitive equality on <see cref="ConfigEntryRecord.Scope"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="environment">Case-insensitive equality on <see cref="ConfigEntryRecord.Environment"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="tenantId">Case-sensitive equality on <see cref="ConfigEntryRecord.TenantId"/>, or <see langword="null"/> for no filter. Empty string matches global-default entries.</param>
    /// <param name="keyPrefix">Case-insensitive starts-with match on <see cref="ConfigEntryRecord.Key"/>, or <see langword="null"/> for no filter.</param>
    /// <param name="take">Maximum number of rows to return. Implementations apply this as a SQL <c>TOP</c> / <c>LIMIT</c> — never as in-memory truncation of a full scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Entries ordered by <c>(Scope, Environment, TenantId, Key)</c> ascending for
    /// deterministic pagination. Secret entries are returned in plaintext after decryption.
    /// </returns>
    Task<IReadOnlyList<ConfigEntryRecord>> QueryAsync(
        string? scope,
        string? environment,
        string? tenantId,
        string? keyPrefix,
        int take,
        CancellationToken ct);
}
