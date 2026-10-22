namespace DbConfig.Core;

/// <summary>
/// A single configuration entry uniquely identified by (Scope, Environment, TenantId, Key).
/// <c>Scope</c> is the same value the host sets via <see cref="DbConfigOptions.Scope"/> and
/// lists in <see cref="DbConfigOptions.IncludeScopes"/>.
/// </summary>
/// <remarks>
/// <see cref="IsWriteOnly"/> marks a value that the HTTP API never returns: it can be created or
/// replaced but not read back. The host still reads it in-process. Stores persist a write-only
/// entry with <see cref="IsSecret"/> forced to <see langword="true"/>, so it is always encrypted
/// at rest. Write-only is not an authorization boundary: every app whose host includes the scope
/// can read the plaintext.
/// </remarks>
public sealed record ConfigEntryRecord(
    string Scope,
    string Environment,
    string TenantId,    // "" for global default
    string Key,
    string? Value,
    bool IsSecret,
    DateTimeOffset ModifiedUtc,
    string? ModifiedBy,
    bool IsWriteOnly = false);
