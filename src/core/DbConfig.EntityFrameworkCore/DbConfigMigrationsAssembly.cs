using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="IMigrationsAssembly"/> override that constructs DbConfig migrations
/// with the configured schema from <see cref="DbConfigOptionsExtension.Schema"/>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's default <see cref="MigrationsAssembly"/> instantiates migrations via
/// <c>Activator.CreateInstance(type)</c> — a no-arg constructor. That makes it impossible
/// for a migration to read runtime config (like the configured schema). This subclass
/// overrides <see cref="CreateMigration"/> to call a <c>(string?)</c>-arg constructor,
/// passing the schema read from the DbContextOptions extension.
/// </para>
/// <para>
/// Registered via <c>options.ReplaceService&lt;IMigrationsAssembly, DbConfigMigrationsAssembly&gt;()</c>
/// inside each provider's <c>UseSqlServer</c>/<c>UseNpgsql</c> wrapper for DbConfig.
/// </para>
/// </remarks>
#pragma warning disable EF1001 // MigrationsAssembly is internal API, but it's the documented extension point
internal sealed class DbConfigMigrationsAssembly : MigrationsAssembly
#pragma warning restore EF1001
{
    private readonly IDbContextOptions _options;

#pragma warning disable EF1001
    public DbConfigMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger)
#pragma warning restore EF1001
    {
        _options = options;
    }

    public override Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        ArgumentNullException.ThrowIfNull(migrationClass);
        ArgumentNullException.ThrowIfNull(activeProvider);

        var schema = _options.GetDbConfigSchema();

        var migration = (Migration)Activator.CreateInstance(migrationClass.AsType(), schema)!;
        migration.ActiveProvider = activeProvider;
        return migration;
    }
}
