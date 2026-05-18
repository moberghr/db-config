namespace DbConfig.Core;

/// <summary>
/// A single configuration entry uniquely identified by (AppName, Environment, TenantId, Key).
/// </summary>
public sealed record ConfigEntry(
    string AppName,
    string Environment,
    string TenantId,    // "" for global default
    string Key,
    string? Value,
    bool IsSecret,
    DateTimeOffset ModifiedUtc,
    string? ModifiedBy);
