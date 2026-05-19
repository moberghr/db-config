using Microsoft.EntityFrameworkCore;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="DbContext"/> for the DbConfig store.
/// Shared across provider packages; each provider configures the connection
/// string via <see cref="DbContextOptions{TContext}"/>.
/// </summary>
public sealed class DbConfigDbContext : DbContext
{
    public DbConfigDbContext(DbContextOptions<DbConfigDbContext> options)
        : base(options)
    {
    }

    internal DbSet<ConfigEntryEntity> ConfigEntries => Set<ConfigEntryEntity>();

    internal DbSet<ConfigAuditEntryEntity> AuditEntries => Set<ConfigAuditEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ConfigEntryEntity>(entity =>
        {
            entity.ToTable("DbConfig_Entries");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .ValueGeneratedNever();

            entity.Property(x => x.AppName)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.Environment)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(x => x.TenantId)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.Key)
                .HasMaxLength(512)
                .IsRequired();

            entity.Property(x => x.Value)
                .IsRequired(false);

            entity.Property(x => x.IsSecret)
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(x => x.ModifiedUtc)
                .HasConversion(new UtcDateTimeConverter())
                .IsRequired();

            entity.Property(x => x.ModifiedBy)
                .HasMaxLength(256)
                .IsRequired(false);

            entity.HasIndex(x => new { x.AppName, x.Environment, x.TenantId, x.Key })
                .IsUnique()
                .HasDatabaseName("UX_DbConfig_Entries_AppName_Environment_TenantId_Key");

            entity.HasIndex(x => new { x.AppName, x.Environment, x.TenantId, x.ModifiedUtc })
                .HasDatabaseName("IX_DbConfig_Entries_AppName_Environment_TenantId_ModifiedUtc");
        });

        modelBuilder.Entity<ConfigAuditEntryEntity>(entity =>
        {
            entity.ToTable("DbConfig_AuditEntries");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .ValueGeneratedNever();

            entity.Property(x => x.AppName)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.Environment)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(x => x.TenantId)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.Key)
                .HasMaxLength(512)
                .IsRequired();

            entity.Property(x => x.OldValue)
                .IsRequired(false);

            entity.Property(x => x.NewValue)
                .IsRequired(false);

            entity.Property(x => x.IsSecret)
                .IsRequired();

            entity.Property(x => x.Action)
                .HasMaxLength(16)
                .IsRequired();

            entity.Property(x => x.ModifiedUtc)
                .HasConversion(new UtcDateTimeOffsetConverter())
                .IsRequired();

            entity.Property(x => x.ModifiedBy)
                .HasMaxLength(256)
                .IsRequired(false);

            entity.HasIndex(x => new { x.AppName, x.Environment, x.TenantId, x.Key, x.ModifiedUtc })
                .HasDatabaseName("IX_DbConfig_Audit_AppName_Environment_TenantId_Key_ModifiedUtc");
        });
    }
}
