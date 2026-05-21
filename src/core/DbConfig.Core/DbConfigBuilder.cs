using Microsoft.Extensions.DependencyInjection;

namespace DbConfig.Core;

/// <summary>
/// Fluent builder passed to the <c>AddDbConfig</c> extension method on
/// <c>IHostApplicationBuilder</c>. Provider packages (e.g. <c>Moberg.DbConfig.Provider.SqlServer</c>)
/// capture their EF Core configuration and unique-constraint detector via the internal capture methods.
/// </summary>
public sealed class DbConfigBuilder
{
    internal DbConfigBuilder(IServiceCollection services, DbConfigOptions options)
    {
        Services = services;
        Options = options;
    }

    /// <summary>
    /// The host <see cref="IServiceCollection"/>. This is the <strong>same instance</strong>
    /// as the host's service collection — services registered here are visible to the entire
    /// application's DI container.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>Polling and scoping options for the configuration provider.</summary>
    public DbConfigOptions Options { get; }

    /// <summary>
    /// Captured EF Core context configuration action. Typed as <c>object?</c> (actually
    /// <c>Action&lt;DbContextOptionsBuilder&gt;</c>) so <c>DbConfig.Core</c> does not take
    /// a hard compile-time dependency on EF Core types. Cast to the concrete type at usage
    /// site in <c>DbConfig.EntityFrameworkCore</c>.
    /// </summary>
    internal object? ConfigureDbContextActionObject { get; private set; }

    /// <summary>
    /// Captured unique-constraint detector instance. Typed as <c>object?</c> (actually
    /// <c>IUniqueConstraintDetector</c>) so <c>DbConfig.Core</c> does not take a hard
    /// compile-time dependency on EF Core types. Cast to the concrete type at usage site
    /// in <c>DbConfig.EntityFrameworkCore</c>.
    /// </summary>
    internal object? DetectorObject { get; private set; }

    /// <summary>
    /// Captured schema-migrator callback. Each provider extension (UseSqlServer/UsePostgreSql)
    /// sets this to its own raw-SQL migrator. Invoked synchronously by <c>AddDbConfig</c>
    /// when <see cref="DbConfigOptions.SchemaMode"/> is <c>CreateIfMissing</c>, before the
    /// configuration source's first <c>Load()</c>. Signature: <c>(schema, ct) =&gt; Task</c>.
    /// </summary>
    internal Func<string?, CancellationToken, Task>? MigratorCallback { get; private set; }

    /// <summary>
    /// Sets the EF Core context configuration action. Throws if called more than once.
    /// The <paramref name="action"/> must be an <c>Action&lt;DbContextOptionsBuilder&gt;</c>.
    /// </summary>
    internal void SetConfigureDbContext(object action)
    {
        if (ConfigureDbContextActionObject is not null)
        {
            throw new InvalidOperationException(
                "Provider extension (UseSqlServer/UsePostgreSql) called more than once on the same DbConfigBuilder.");
        }

        ConfigureDbContextActionObject = action;
    }

    /// <summary>
    /// Sets the unique-constraint detector. Throws if called more than once.
    /// The <paramref name="detector"/> must implement <c>IUniqueConstraintDetector</c>.
    /// </summary>
    internal void SetDetector(object detector)
    {
        if (DetectorObject is not null)
        {
            throw new InvalidOperationException("Detector already set.");
        }

        DetectorObject = detector;
    }

    /// <summary>
    /// Sets the schema-migrator callback. Throws if called more than once.
    /// Called by provider extensions to wire their raw-SQL migrator.
    /// </summary>
    internal void SetMigrator(Func<string?, CancellationToken, Task> migrator)
    {
        if (MigratorCallback is not null)
        {
            throw new InvalidOperationException("Migrator callback already set.");
        }

        MigratorCallback = migrator;
    }

    /// <summary>
    /// Registers a tenant resolver for tenant-aware config lookups. The framework calls
    /// <see cref="ITenantResolver.Resolve"/> at each <c>IConfiguration[key]</c> read; a
    /// non-null result selects the tenant-specific entry (with fallback to global).
    /// </summary>
    /// <typeparam name="TResolver">The resolver implementation type. Resolved from
    /// host DI; can take constructor dependencies (e.g. <c>IHttpContextAccessor</c>).</typeparam>
    public DbConfigBuilder AddTenantResolver<TResolver>()
        where TResolver : class, ITenantResolver
    {
        Services.AddSingleton<ITenantResolver, TResolver>();
        return this;
    }
}
