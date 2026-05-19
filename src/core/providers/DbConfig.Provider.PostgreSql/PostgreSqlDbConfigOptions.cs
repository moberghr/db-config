using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.Provider.PostgreSql;

/// <summary>
/// Helpers for constructing <see cref="DbContextOptions{TContext}"/> for the DbConfig
/// PostgreSQL provider, pre-wired with the correct migrations assembly. Pass the result
/// to <see cref="DbConfigMigrator"/>.
/// </summary>
public static class PostgreSqlDbConfigOptions
{
    /// <summary>
    /// Builds <see cref="DbContextOptions{TContext}"/> configured for PostgreSQL, with
    /// the correct migrations assembly registered.
    /// </summary>
    public static DbContextOptions<DbConfigDbContext> ForPostgreSql(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        return new DbContextOptionsBuilder<DbConfigDbContext>()
            .UseNpgsql(
                connectionString,
                npg => npg.MigrationsAssembly("DbConfig.Provider.PostgreSql"))
            .Options;
    }
}
