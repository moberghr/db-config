namespace DbConfig.Core;

/// <summary>
/// Controls how DbConfig manages the underlying database schema at host startup.
/// </summary>
public enum SchemaMode
{
    /// <summary>
    /// On host startup, run the provider's idempotent raw-SQL migrator
    /// (<c>SqlServerDbConfigMigrator.MigrateAsync</c> or
    /// <c>PostgreSqlDbConfigMigrator.MigrateAsync</c>). Default. Matches
    /// Hangfire / Marten / Wolverine conventions.
    /// </summary>
    CreateIfMissing,

    /// <summary>
    /// Skip schema management entirely. The host assumes the schema is already correct
    /// (typically because a DBA or CI/CD pipeline applied it out of band). Use the
    /// per-provider <c>GetCreateScript(schema)</c> helper to obtain the SQL for
    /// offline application.
    /// </summary>
    None,
}
