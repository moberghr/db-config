using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.Provider.SqlServer;

/// <summary>
/// Helpers for constructing <see cref="DbContextOptions{TContext}"/> for the DbConfig
/// SQL Server provider, pre-wired with the configured schema. Use when you need a
/// stand-alone <see cref="DbConfigDbContext"/> (e.g. integration tests). For schema
/// management see <see cref="SqlServerDbConfigMigrator"/>.
/// </summary>
public static class SqlServerDbConfigOptions
{
    /// <summary>
    /// Builds <see cref="DbContextOptions{TContext}"/> configured for SQL Server.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="schema">Database schema for DbConfig tables. Defaults to <c>"configuration"</c>;
    /// pass <see langword="null"/> to use the database default (<c>dbo</c>).</param>
    public static DbContextOptions<DbConfigDbContext> ForSqlServer(
        string connectionString,
        string? schema = "configuration")
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        return new DbContextOptionsBuilder<DbConfigDbContext>()
            .UseSqlServer(connectionString)
            .UseDbConfigSchema(schema)
            .Options;
    }
}
