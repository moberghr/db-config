namespace DbConfig.Core;

/// <summary>
/// Configuration options for the database-backed configuration provider.
/// </summary>
public sealed class DbConfigOptions
{
    /// <summary>Gets or sets the application name used to scope configuration entries.</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Gets or sets the environment name used to scope configuration entries.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Gets or sets how frequently the provider polls the store for changes. Default is 30 seconds.</summary>
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Additional AppName scopes to include in polling reads, ordered lowest-precedence-first.
    /// The configured <see cref="AppName"/> is always read with highest precedence (last-writer-wins).
    /// Empty by default. Example: ["PlatformDefaults", "Shared"] yields effective precedence
    /// PlatformDefaults &lt; Shared &lt; AppName.
    /// </summary>
    public IReadOnlyList<string> IncludeScopes { get; set; } = [];

    /// <summary>
    /// When <see langword="true"/> (the default), every Upsert and Delete operation writes an
    /// audit row in the same <c>SaveChangesAsync</c> as the mutation.  Set to
    /// <see langword="false"/> to opt out of audit logging entirely (no <c>DbConfig_AuditEntries</c>
    /// rows are written and no performance cost is incurred).
    /// </summary>
    public bool EnableAuditLog { get; set; } = true;

    /// <summary>
    /// When true, HTTP GET endpoints (single and list) write fire-and-forget audit rows
    /// with Action=Read, OldValue=null, NewValue=null. Default false. The audit history
    /// endpoint never generates read audit rows (avoids recursion). Failures to write
    /// read audits log a warning but do not fail the GET.
    /// </summary>
    public bool AuditReads { get; set; } = false;

    /// <summary>
    /// How DbConfig manages the underlying database schema at host startup.
    /// Default: <see cref="SchemaMode.CreateIfMissing"/> — auto-migrate.
    /// </summary>
    public SchemaMode SchemaMode { get; set; } = SchemaMode.CreateIfMissing;
}
