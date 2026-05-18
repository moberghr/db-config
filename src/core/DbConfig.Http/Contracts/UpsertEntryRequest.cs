namespace DbConfig.Http;

/// <summary>Request body for the PUT (upsert) endpoint.</summary>
/// <param name="Value">The configuration value to store.</param>
/// <param name="IsSecret">Whether the value should be encrypted at rest.</param>
/// <param name="TenantId">
/// Omit or pass empty string to upsert the global default entry;
/// pass a non-empty tenant id to upsert a tenant-specific entry.
/// </param>
public sealed record UpsertEntryRequest(string? Value, bool IsSecret, string? TenantId = null);
