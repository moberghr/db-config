namespace DbConfig.Http;

/// <summary>Request body for the PUT (upsert) endpoint.</summary>
/// <param name="Value">The configuration value to store.</param>
/// <param name="IsSecret">Whether the value should be encrypted at rest.</param>
/// <param name="TenantId">
/// Omit or pass empty string to upsert the global default entry;
/// pass a non-empty tenant id to upsert a tenant-specific entry.
/// </param>
/// <param name="IsWriteOnly">
/// Whether the value can only be written over HTTP, never read back. Implies
/// <paramref name="IsSecret"/>. Omit (or pass <see langword="null"/>) to keep the existing entry's
/// flag, so clients unaware of write-only never downgrade one by accident. Sending
/// <see langword="false"/> explicitly clears the flag; the request's <paramref name="Value"/>
/// replaces the stored one, so the old value is never revealed.
/// </param>
public sealed record UpsertEntryRequest(string? Value, bool IsSecret, string? TenantId = null, bool? IsWriteOnly = null);
