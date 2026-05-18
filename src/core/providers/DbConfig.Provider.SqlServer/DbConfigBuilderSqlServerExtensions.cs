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
    /// Configures the DbConfig store to use SQL Server via EF Core.
    /// Migrations are located in the <c>DbConfig.Provider.SqlServer</c> assembly.
    /// </summary>
    public static DbConfigBuilder UseSqlServer(this DbConfigBuilder builder, string connectionString)
    {
        builder.SetDetector(new SqlServerUniqueConstraintDetector());

        return builder.UseEntityFrameworkCore(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer")));
    }
}
