using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DbConfig.Provider.PostgreSql;

/// <summary>
/// Helpers for constructing <see cref="DbContextOptions{TContext}"/> for the DbConfig
/// PostgreSQL provider, pre-wired with the correct migrations assembly, schema,
/// snake_case naming convention, and custom <c>IMigrationsAssembly</c>. Pass the result
/// to <see cref="DbConfigMigrator"/>.
/// </summary>
public static class PostgreSqlDbConfigOptions
{
    /// <summary>
    /// Builds <see cref="DbContextOptions{TContext}"/> configured for PostgreSQL.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="schema">Database schema for DbConfig tables. Defaults to <c>"configuration"</c>;
    /// pass <see langword="null"/> to use the database default (<c>public</c>).</param>
    public static DbContextOptions<DbConfigDbContext> ForPostgreSql(
        string connectionString,
        string? schema = "configuration")
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        return new DbContextOptionsBuilder<DbConfigDbContext>()
            .UseNpgsql(
                connectionString,
                npg => npg.MigrationsAssembly("DbConfig.Provider.PostgreSql"))
            .UseSnakeCaseNamingConvention()
            .UseDbConfigSchema(schema)
            .ReplaceService<IMigrationsAssembly, DbConfigMigrationsAssembly>()
            .Options;
    }
}
