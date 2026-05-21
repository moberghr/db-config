using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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

    /// <summary>
    /// Replaces the default <see cref="IModelCacheKeyFactory"/> so the cached model varies
    /// with <see cref="DbConfigOptionsExtension.Schema"/>. Without this, the first DbContext
    /// build caches a model for whatever schema it sees first, and subsequent contexts built
    /// with a different schema would reuse the stale model. Required for tests that mount
    /// multiple hosts with different schemas in one process.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, DbConfigModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply the configured schema (set via DbContextOptionsBuilder.UseDbConfigSchema or
        // by provider helpers). Null means "use the database's default schema".
        var schema = this.GetService<IDbContextOptions>().GetDbConfigSchema();
        if (schema is not null)
        {
            modelBuilder.HasDefaultSchema(schema);
        }

        modelBuilder.Entity<ConfigEntryEntity>(entity =>
        {
            entity.ToTable("DbConfig_Entries");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .ValueGeneratedNever();

            entity.Property(x => x.Scope)
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
                .HasConversion(new UtcDateTimeOffsetConverter())
                .IsRequired();

            entity.Property(x => x.ModifiedBy)
                .HasMaxLength(256)
                .IsRequired(false);

            entity.HasIndex(x => new { x.Scope, x.Environment, x.TenantId, x.Key })
                .IsUnique()
                .HasDatabaseName("UX_DbConfig_Entries_Scope_Environment_TenantId_Key");

            entity.HasIndex(x => new { x.Scope, x.Environment, x.TenantId, x.ModifiedUtc })
                .HasDatabaseName("IX_DbConfig_Entries_Scope_Environment_TenantId_ModifiedUtc");
        });

        modelBuilder.Entity<ConfigAuditEntryEntity>(entity =>
        {
            entity.ToTable("DbConfig_AuditEntries");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .ValueGeneratedNever();

            entity.Property(x => x.Scope)
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

            entity.HasIndex(x => new { x.Scope, x.Environment, x.TenantId, x.Key, x.ModifiedUtc })
                .HasDatabaseName("IX_DbConfig_Audit_Scope_Environment_TenantId_Key_ModifiedUtc");
        });
    }
}
