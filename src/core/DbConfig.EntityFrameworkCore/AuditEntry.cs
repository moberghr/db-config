namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// EF Core entity mapped by EF default conventions to the <c>AuditEntries</c>
/// (SQL Server) or <c>audit_entries</c> (PostgreSQL, via snake_case naming) table.
/// The table name comes from the <see cref="DbConfigDbContext.AuditEntries"/> DbSet
/// property name (plural). Values are stored as ciphertext when <see cref="IsSecret"/>
/// is <see langword="true"/>; decryption is applied by <c>EfCoreConfigAuditStore</c>
/// before returning records to callers.
/// </summary>
internal sealed class AuditEntry
{
    public Guid Id { get; set; }

    public string Scope { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>Ciphertext when <see cref="IsSecret"/> is <see langword="true"/>; plaintext otherwise.</summary>
    public string? OldValue { get; set; }

    /// <summary>Ciphertext when <see cref="IsSecret"/> is <see langword="true"/>; plaintext otherwise. <see langword="null"/> for Delete actions.</summary>
    public string? NewValue { get; set; }

    public bool IsSecret { get; set; }

    /// <summary>Stored as a string ('Insert', 'Update', 'Delete') for migration friendliness.</summary>
    public string Action { get; set; } = string.Empty;

    public DateTimeOffset ModifiedUtc { get; set; }

    public string? ModifiedBy { get; set; }
}
