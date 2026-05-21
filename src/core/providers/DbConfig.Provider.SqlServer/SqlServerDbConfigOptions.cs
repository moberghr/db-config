using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DbConfig.Provider.SqlServer;

/// <summary>
/// Helpers for constructing <see cref="DbContextOptions{TContext}"/> for the DbConfig
/// SQL Server provider, pre-wired with the correct migrations assembly, schema, and
/// custom <c>IMigrationsAssembly</c>. Pass the result to <see cref="DbConfigMigrator"/>.
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
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer"))
            .UseDbConfigSchema(schema)
            .ReplaceService<IMigrationsAssembly, DbConfigMigrationsAssembly>()
            .Options;
    }
}
