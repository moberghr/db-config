namespace DbConfig.Core;

/// <summary>
/// Configuration options for the database-backed configuration provider.
/// </summary>
public sealed class DbConfigOptions
{
    /// <summary>
    /// Gets or sets the scope (logical application name) used as the primary bucket for
    /// configuration entries. Stored in the <c>Scope</c> column of the <c>ConfigEntries</c>
    /// (SQL Server) / <c>config_entries</c> (PostgreSQL) table.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the environment name used to scope configuration entries.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Gets or sets how frequently the provider polls the store for changes. Default is 30 seconds.</summary>
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Additional scopes to include in polling reads, ordered lowest-precedence-first.
    /// The configured <see cref="Scope"/> is always read with highest precedence (last-writer-wins).
    /// Empty by default. Example: ["PlatformDefaults", "Shared"] yields effective precedence
    /// PlatformDefaults &lt; Shared &lt; <see cref="Scope"/>.
    /// </summary>
    public IReadOnlyList<string> IncludeScopes { get; set; } = [];

    /// <summary>
    /// When <see langword="true"/> (the default), every Upsert and Delete operation writes an
    /// audit row in the same <c>SaveChangesAsync</c> as the mutation.  Set to
    /// <see langword="false"/> to opt out of audit logging entirely (no rows written to
    /// the <c>AuditEntries</c> / <c>audit_entries</c> table and no performance cost is incurred).
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

    /// <summary>
    /// Database schema (namespace) for the DbConfig tables. Default: <c>"configuration"</c>.
    /// Set to <see langword="null"/> to use the database's default schema (<c>dbo</c> on SQL
    /// Server, <c>public</c> on PostgreSQL). The schema is created automatically when
    /// <see cref="SchemaMode"/> is <see cref="SchemaMode.CreateIfMissing"/>. On PostgreSQL
    /// the value MUST be a snake_case identifier (lowercase letters/digits/underscore, leading
    /// non-digit) because the runtime model applies <c>UseSnakeCaseNamingConvention</c>;
    /// a non-snake schema would cause the runtime model and the on-disk DDL to disagree.
    /// </summary>
    public string? Schema { get; set; } = "configuration";
}
