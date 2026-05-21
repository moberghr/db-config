namespace DbConfig.Core;

/// <summary>
/// A single configuration entry uniquely identified by (Scope, Environment, TenantId, Key).
/// <c>Scope</c> is the same value the host sets via <see cref="DbConfigOptions.Scope"/> and
/// lists in <see cref="DbConfigOptions.IncludeScopes"/>.
/// </summary>
public sealed record ConfigEntryRecord(
    string Scope,
    string Environment,
    string TenantId,    // "" for global default
    string Key,
    string? Value,
    bool IsSecret,
    DateTimeOffset ModifiedUtc,
    string? ModifiedBy);
