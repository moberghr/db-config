using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.Provider.PostgreSql;

/// <summary>
/// Extension methods on <see cref="DbConfigBuilder"/> for PostgreSQL.
/// </summary>
public static class DbConfigBuilderPostgreSqlExtensions
{
    /// <summary>
    /// Configures the DbConfig store to use PostgreSQL via EF Core.
    /// Migrations are located in the <c>DbConfig.Provider.PostgreSql</c> assembly.
    /// Uses snake_case naming convention for tables, columns, and indexes — PG-idiomatic.
    /// </summary>
    public static DbConfigBuilder UsePostgreSql(this DbConfigBuilder builder, string connectionString)
    {
        builder.SetDetector(new PostgreSqlUniqueConstraintDetector());

        return builder.UseEntityFrameworkCore(options =>
            options
                .UseNpgsql(
                    connectionString,
                    npg => npg.MigrationsAssembly("DbConfig.Provider.PostgreSql"))
                .UseSnakeCaseNamingConvention());
    }
}
