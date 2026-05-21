namespace DbConfig.Core;

/// <summary>
/// Cheap change-detection watermark queries used by the polling provider to decide
/// whether a full reload is needed.
/// </summary>
/// <remarks>
/// Implementations issue <c>MAX(ModifiedUtc)</c> against the appropriate index slice;
/// no entries are returned. Each variant covers a different scoping shape (single scope
/// vs scope set; global-only vs single tenant vs across all tenants). The polling
/// provider picks the variant that matches its configured scope and tenant model.
/// </remarks>
public interface IConfigWatermark
{
    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> value across all
    /// global (<c>TenantId = ""</c>) entries for the given scope and environment, or
    /// <see langword="null"/> if there are no entries.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string scope, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> across all global
    /// (<c>TenantId = ""</c>) entries matching any <c>(scope ∈ scopes, environment)</c>
    /// pair, or <see langword="null"/> if there are no such entries.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> across all entries
    /// for the given scope, environment, and tenant, or <see langword="null"/> if there
    /// are none.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
        string scope, string environment, string tenantId, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> across ALL entries
    /// for the given scope and environment, regardless of tenant (including
    /// <c>TenantId = ""</c>). Used by the polling provider as a change-detection
    /// watermark that covers every tenant. Returns <see langword="null"/> if no entries
    /// exist.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
        string scope, string environment, CancellationToken ct);

    /// <summary>
    /// Returns the highest <see cref="ConfigEntryRecord.ModifiedUtc"/> across ALL entries
    /// (every tenant) whose Scope is in <paramref name="scopes"/> in the given environment.
    /// Used as a multi-scope, multi-tenant watermark for change detection.
    /// </summary>
    Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct);
}
