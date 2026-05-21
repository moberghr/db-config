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
    /// Configures the DbConfig store to use PostgreSQL. Schema management uses a raw-SQL
    /// idempotent script embedded in this assembly; EF Core migrations are not used.
    /// Applies <c>UseSnakeCaseNamingConvention</c> so the runtime EF model maps the
    /// PascalCase entity properties to the snake_case identifiers the script creates
    /// (<c>config_entry</c>, <c>tenant_id</c>, <c>pk_db_config_entries</c>, …).
    /// </summary>
    public static DbConfigBuilder UsePostgreSql(this DbConfigBuilder builder, string connectionString)
    {
        builder.SetDetector(new PostgreSqlUniqueConstraintDetector());
        builder.SetMigrator((schema, ct) =>
            PostgreSqlDbConfigMigrator.MigrateAsync(connectionString, schema, ct));

        return builder.UseEntityFrameworkCore(options =>
            options
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());
    }
}
