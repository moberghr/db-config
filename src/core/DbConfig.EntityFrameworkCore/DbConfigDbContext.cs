using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="DbContext"/> for the DbConfig store.
/// Shared across provider packages; each provider configures the connection
/// string via <see cref="DbContextOptions{TContext}"/>.
/// </summary>
/// <remarks>
/// <para>Identifiers come from EF Core defaults — the class name drives the table name,
/// property names drive column names, and indexes auto-name from the columns they cover.
/// Per-provider casing comes from the provider's options pipeline: PostgreSQL applies
/// <c>UseSnakeCaseNamingConvention</c> so <c>ConfigEntry</c> becomes <c>config_entry</c>,
/// <c>TenantId</c> becomes <c>tenant_id</c>, and so on. SQL Server keeps PascalCase.</para>
/// <para>OnModelCreating contains NO name literals on purpose — explicit <c>ToTable</c>
/// or <c>HasColumnName</c> strings would defeat the convention rewriter and produce
/// runtime queries that target the wrong identifiers on PostgreSQL.</para>
/// </remarks>
public sealed class DbConfigDbContext : DbContext
{
    public DbConfigDbContext(DbContextOptions<DbConfigDbContext> options)
        : base(options)
    {
    }

    internal DbSet<ConfigEntry> ConfigEntries => Set<ConfigEntry>();

    internal DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply the configured schema (via DbContextOptionsBuilder.UseDbConfigSchema or
        // by provider helpers). Null means "use the database's default schema".
        var schema = this.GetService<IDbContextOptions>().GetDbConfigSchema();
        if (schema is not null)
        {
            modelBuilder.HasDefaultSchema(schema);
        }

        modelBuilder.Entity<ConfigEntry>(entity =>
        {
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
                .IsUnique();

            entity.HasIndex(x => new { x.Scope, x.Environment, x.TenantId, x.ModifiedUtc });
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
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

            entity.HasIndex(x => new { x.Scope, x.Environment, x.TenantId, x.Key, x.ModifiedUtc });
        });
    }
}
