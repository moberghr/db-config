using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.Provider.PostgreSql;

/// <summary>
/// Helpers for constructing <see cref="DbContextOptions{TContext}"/> for the DbConfig
/// PostgreSQL provider, pre-wired with the configured schema and snake_case naming
/// convention. Use when you need a stand-alone <see cref="DbConfigDbContext"/>
/// (e.g. integration tests). For schema management see <see cref="PostgreSqlDbConfigMigrator"/>.
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
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .UseDbConfigSchema(schema)
            .Options;
    }
}
