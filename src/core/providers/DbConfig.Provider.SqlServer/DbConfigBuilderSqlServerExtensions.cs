using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.Provider.SqlServer;

/// <summary>
/// Extension methods on <see cref="DbConfigBuilder"/> for SQL Server.
/// </summary>
public static class DbConfigBuilderSqlServerExtensions
{
    /// <summary>
    /// Configures the DbConfig store to use SQL Server. Schema management uses a raw-SQL
    /// idempotent script embedded in this assembly; EF Core migrations are not used.
    /// </summary>
    public static DbConfigBuilder UseSqlServer(this DbConfigBuilder builder, string connectionString)
    {
        builder.SetDetector(new SqlServerUniqueConstraintDetector());
        builder.SetMigrator((schema, ct) =>
            SqlServerDbConfigMigrator.MigrateAsync(connectionString, schema, ct));

        return builder.UseEntityFrameworkCore(options =>
            options.UseSqlServer(connectionString));
    }
}
