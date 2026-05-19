using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.Provider.SqlServer;

/// <summary>
/// Helpers for constructing <see cref="DbContextOptions{TContext}"/> for the DbConfig
/// SQL Server provider, pre-wired with the correct migrations assembly. Pass the result
/// to <see cref="DbConfigMigrator"/>.
/// </summary>
public static class SqlServerDbConfigOptions
{
    /// <summary>
    /// Builds <see cref="DbContextOptions{TContext}"/> configured for SQL Server, with
    /// the correct migrations assembly registered.
    /// </summary>
    public static DbContextOptions<DbConfigDbContext> ForSqlServer(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        return new DbContextOptionsBuilder<DbConfigDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer"))
            .Options;
    }
}
