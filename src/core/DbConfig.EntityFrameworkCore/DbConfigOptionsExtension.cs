using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// Carries DbConfig-specific configuration (currently just the table <see cref="Schema"/>)
/// inside the standard EF Core <see cref="DbContextOptions"/> pipeline. EF Core's official
/// mechanism for passing config through to the DbContext, migration assembly, and other
/// services that resolve via the internal service provider.
/// </summary>
/// <remarks>
/// Replacing this with a process-wide static would be simpler but introduces shared
/// mutable state across hosts/tests — see CLAUDE.md §2.10 for why we keep state local to
/// the host's options instance.
/// </remarks>
public sealed class DbConfigOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbConfigOptionsExtension()
    {
    }

    private DbConfigOptionsExtension(DbConfigOptionsExtension copyFrom)
    {
        Schema = copyFrom.Schema;
    }

    /// <summary>
    /// Database schema for the <c>ConfigEntry</c> and <c>AuditEntry</c>
    /// tables. <see langword="null"/> means use the database default
    /// (<c>dbo</c> on SQL Server, <c>public</c> on PostgreSQL).
    /// </summary>
    public string? Schema { get; private init; }

    /// <inheritdoc/>
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>
    /// Returns a copy of this extension with the schema replaced. Follows EF Core's
    /// immutable-extension pattern — call <see cref="WithSchema"/> instead of mutating.
    /// </summary>
    public DbConfigOptionsExtension WithSchema(string? schema)
        => new(this) { Schema = schema };

    /// <inheritdoc/>
    public void ApplyServices(IServiceCollection services)
    {
        // The extension carries data (Schema) only — no EF Core internal services to register.
        // EF Core requires the method on every IDbContextOptionsExtension, so this is an
        // intentional no-op.
    }

    /// <inheritdoc/>
    public void Validate(IDbContextOptions options)
    {
        // No validation needed — null Schema is valid (means DB default), and any non-null
        // string is accepted (provider rejects invalid identifiers at apply time).
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        private readonly DbConfigOptionsExtension _extension;

        public ExtensionInfo(DbConfigOptionsExtension extension)
            : base(extension)
        {
            _extension = extension;
        }

        public override bool IsDatabaseProvider => false;

        public override int GetServiceProviderHashCode()
            => _extension.Schema?.GetHashCode(StringComparison.Ordinal) ?? 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo o && string.Equals(_extension.Schema, o._extension.Schema, StringComparison.Ordinal);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["DbConfig:Schema"] = _extension.Schema ?? "(default)";
        }

        public override string LogFragment => $"using DbConfig schema={_extension.Schema ?? "(default)"} ";
    }
}

/// <summary>
/// <see cref="DbContextOptionsBuilder"/> extensions to apply DbConfig-specific options
/// (currently the <see cref="DbConfigOptionsExtension.Schema"/> for table placement).
/// </summary>
public static class DbConfigOptionsExtensionBuilderExtensions
{
    /// <summary>
    /// Sets the schema for the DbConfig tables. <see langword="null"/> uses the database
    /// default (<c>dbo</c> on SQL Server, <c>public</c> on PostgreSQL).
    /// </summary>
    public static DbContextOptionsBuilder UseDbConfigSchema(this DbContextOptionsBuilder builder, string? schema)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var existing = builder.Options.FindExtension<DbConfigOptionsExtension>() ?? new DbConfigOptionsExtension();
        var updated = existing.WithSchema(schema);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(updated);
        return builder;
    }

    /// <summary>
    /// Generic overload that preserves the typed <see cref="DbContextOptionsBuilder{TContext}"/>
    /// through the fluent chain.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseDbConfigSchema<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string? schema)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDbConfigSchema((DbContextOptionsBuilder)builder, schema);

    internal static string? GetDbConfigSchema(this IDbContextOptions options)
        => options.FindExtension<DbConfigOptionsExtension>()?.Schema;
}
