namespace DbConfig.Core;

/// <summary>
/// The action that produced an audit entry.
/// </summary>
public enum ConfigAuditAction
{
    /// <summary>A new entry was inserted.</summary>
    Insert,

    /// <summary>An existing entry was updated.</summary>
    Update,

    /// <summary>An existing entry was deleted.</summary>
    Delete,

    /// <summary>An entry (or list) was read. Written when <see cref="DbConfigOptions.AuditReads"/> is enabled.</summary>
    Read,
}

/// <summary>
/// An immutable snapshot of a single audit log record capturing a mutation on a
/// <see cref="ConfigEntry"/>. Values returned by <see cref="IConfigAuditStore"/> reads are
/// always plaintext; the store handles decryption internally for entries where
/// <see cref="IsSecret"/> is <see langword="true"/>.
/// </summary>
public sealed record ConfigAuditEntry(
    Guid Id,
    string AppName,
    string Environment,
    string TenantId,    // "" for global default
    string Key,
    string? OldValue,
    string? NewValue,
    bool IsSecret,
    ConfigAuditAction Action,
    DateTimeOffset ModifiedUtc,
    string? ModifiedBy);
