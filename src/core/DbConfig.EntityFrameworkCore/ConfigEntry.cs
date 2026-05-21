namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// EF Core entity mapped by EF default conventions to the <c>ConfigEntries</c>
/// (SQL Server) or <c>config_entries</c> (PostgreSQL, via snake_case naming) table.
/// The table name comes from the <see cref="DbConfigDbContext.ConfigEntries"/> DbSet
/// property name (plural), not the entity class name. The public
/// <see cref="DbConfig.Core.ConfigEntryRecord"/> record is the immutable contract;
/// this class is the mutable EF Core representation.
/// </summary>
internal sealed class ConfigEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Scope { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public bool IsSecret { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }

    public string? ModifiedBy { get; set; }
}
