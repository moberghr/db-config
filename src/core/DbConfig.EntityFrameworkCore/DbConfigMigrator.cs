using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// Static helpers for managing the DbConfig database schema outside of the
/// <c>AddDbConfig</c> auto-create path. Useful for DBA-controlled or CI/CD
/// deployment workflows where the schema is applied out of band.
/// </summary>
/// <remarks>
/// The <see cref="DbContextOptions"/> passed to these helpers MUST have the DbConfig
/// schema applied via <c>DbContextOptionsBuilder.UseDbConfigSchema(...)</c> — the provider
/// helpers (<c>SqlServerDbConfigOptions.ForSqlServer</c>, <c>PostgreSqlDbConfigOptions.ForPostgreSql</c>)
/// do this for you via their <c>schema</c> parameter.
/// </remarks>
public static class DbConfigMigrator
{
    /// <summary>
    /// Applies any pending DbConfig migrations to the database. Idempotent.
    /// </summary>
    public static async Task MigrateAsync(
        DbContextOptions<DbConfigDbContext> options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        await using var ctx = new DbConfigDbContext(options);
        await ctx.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the full create-DDL for the DbConfig schema as a SQL string.
    /// Useful for setting up a fresh database via DBA-controlled scripts.
    /// </summary>
    public static string GenerateCreateScript(
        DbContextOptions<DbConfigDbContext> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var ctx = new DbConfigDbContext(options);
        return ctx.Database.GenerateCreateScript();
    }

    /// <summary>
    /// Returns the SQL needed to upgrade the database between two migrations.
    /// Default: from the current applied state to the latest. With
    /// <paramref name="idempotent"/> = <see langword="true"/>, the SQL is safe
    /// to re-apply (includes guards based on <c>__EFMigrationsHistory</c>).
    /// </summary>
    public static string GenerateMigrationScript(
        DbContextOptions<DbConfigDbContext> options,
        string? fromMigration = null,
        string? toMigration = null,
        bool idempotent = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var ctx = new DbConfigDbContext(options);
        var migrator = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
        var sqlOptions = idempotent
            ? MigrationsSqlGenerationOptions.Idempotent
            : MigrationsSqlGenerationOptions.Default;

        return migrator.GenerateScript(fromMigration, toMigration, sqlOptions);
    }
}
