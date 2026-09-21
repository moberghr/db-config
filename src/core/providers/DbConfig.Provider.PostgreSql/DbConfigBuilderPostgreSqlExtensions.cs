using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        builder.SetDetector(new PostgreSqlUniqueConstraintDetector());
        builder.SetMigrator((schema, ct) =>
            PostgreSqlDbConfigMigrator.MigrateAsync(connectionString, schema, ct));

        return builder.UseEntityFrameworkCore(options =>
            options
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());
    }

    /// <summary>
    /// Configures the DbConfig store to use PostgreSQL through a caller-owned
    /// <see cref="NpgsqlDataSource"/>. Every connection DbConfig opens — the polling provider's,
    /// the HTTP layer's and the startup migrator's — comes from <paramref name="dataSource"/>, so
    /// whatever the data source was built with applies to all of them: a password provider that
    /// fetches an Entra ID token, TLS settings, type mappings, logging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the overload for hosts whose database does not accept a static password. Azure
    /// Database for PostgreSQL with Entra authentication is the common case: the connection string
    /// carries no password and each connection presents a short-lived token instead, which a raw
    /// connection string cannot express. Build the data source with
    /// <c>NpgsqlDataSourceBuilder.UsePasswordProvider</c> (or let Aspire's
    /// <c>AddAzureNpgsqlDataSource</c> build it) and pass it here.
    /// </para>
    /// <para>
    /// The data source must exist before <c>AddDbConfig</c> is called, because the configuration
    /// provider's first load runs synchronously inside it. DbConfig does not dispose the data
    /// source; the caller owns its lifetime.
    /// </para>
    /// </remarks>
    public static DbConfigBuilder UsePostgreSql(this DbConfigBuilder builder, NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dataSource);

        builder.SetDetector(new PostgreSqlUniqueConstraintDetector());
        builder.SetMigrator((schema, ct) =>
            PostgreSqlDbConfigMigrator.MigrateAsync(dataSource, schema, ct));

        return builder.UseEntityFrameworkCore(options =>
            options
                .UseNpgsql(dataSource)
                .UseSnakeCaseNamingConvention());
    }
}
