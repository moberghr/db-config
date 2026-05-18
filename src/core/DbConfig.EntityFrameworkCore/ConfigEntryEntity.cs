namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// EF Core entity that maps to the <c>DbConfig_Entries</c> table.
/// The public <see cref="DbConfig.Core.ConfigEntry"/> record is the immutable contract;
/// this class is the mutable EF Core representation.
/// </summary>
internal sealed class ConfigEntryEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AppName { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public bool IsSecret { get; set; }

    public DateTime ModifiedUtc { get; set; }

    public string? ModifiedBy { get; set; }
}
