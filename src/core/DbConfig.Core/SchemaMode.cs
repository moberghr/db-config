namespace DbConfig.Core;

/// <summary>
/// Controls how DbConfig manages the underlying database schema at host startup.
/// </summary>
public enum SchemaMode
{
    /// <summary>
    /// On host startup, automatically apply any pending EF Core migrations from the
    /// provider package. Default. Matches Hangfire / Marten / Wolverine conventions.
    /// </summary>
    CreateIfMissing,

    /// <summary>
    /// Skip migration entirely. The host assumes the schema is already correct
    /// (typically because a DBA or CI/CD pipeline applied it out of band).
    /// Use <c>DbConfigMigrator.GenerateMigrationScript</c> to obtain SQL for
    /// offline application.
    /// </summary>
    None,
}
