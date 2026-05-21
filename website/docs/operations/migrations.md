---
sidebar_position: 1
---

# Migrations

DbConfig manages its own database schema via a small, hand-written, idempotent SQL
script per provider. **The schema is applied automatically on host startup** by default —
you do not write the script, and you do not have to apply it manually.

This page covers the two `SchemaMode` options, the per-provider migrator helpers, and
the configurable schema name.

## Where the scripts live

Each provider package ships its own embedded SQL script:

- `Moberg.DbConfig.Provider.SqlServer` ships `Sql/InitialCreate.sql` (T-SQL)
- `Moberg.DbConfig.Provider.PostgreSql` ships `Sql/InitialCreate.sql` (Postgres SQL)

Both scripts are idempotent — every `CREATE` statement is guarded with `IF NOT EXISTS`
(or its T-SQL equivalent) so the script can be applied repeatedly without error. The
`{schema}` token in the script body is substituted with the configured schema name at
apply time.

There is no EF Core migration tooling, no `__EFMigrationsHistory` table, no
`dotnet ef migrations add`. v0.13.0 deliberately stepped away from EF Core's migration
system in favor of plain SQL — the library's schema is small enough that hand-written
DDL is simpler than the EF tooling.

## What the script creates

| Object | SQL Server | PostgreSQL |
|---|---|---|
| Main table | `ConfigEntries` | `config_entries` |
| Audit table | `AuditEntries` | `audit_entries` |
| Primary key | `PK_ConfigEntries`, `PK_AuditEntries` | `pk_config_entries`, `pk_audit_entries` |
| Unique constraint | `IX_ConfigEntries_Scope_Environment_TenantId_Key` | `ix_config_entries_scope_environment_tenant_id_key` |
| Watermark index | `IX_ConfigEntries_Scope_Environment_TenantId_ModifiedUtc` | `ix_config_entries_scope_environment_tenant_id_modified_utc` |
| Audit history index | `IX_AuditEntries_Scope_Environment_TenantId_Key_ModifiedUtc` | `ix_audit_entries_scope_environment_tenant_id_key_modified_utc` |

Scoping columns (`Scope`, `Environment`, `TenantId`, `Key`) are created with
case-sensitive collation:

- **SQL Server:** `Latin1_General_100_BIN2`
- **PostgreSQL:** `"C"`

`"MyApp"` and `"myapp"` are therefore distinct scope names. Use consistent casing in
all your `AddDbConfig` registrations, `scopeFilter` values, and tenant ids.

Application code never types any of these identifiers — only matter if you query the
tables directly with `psql` / SSMS / a BI tool. The library's EF runtime model maps the
entity classes to these names automatically (SQL Server uses EF defaults; PostgreSQL
applies `UseSnakeCaseNamingConvention`).

## Configurable schema

`DbConfigOptions.Schema` controls which database schema the tables live in. The default
is `"configuration"`. Set it to a custom string or `null` to use the database default:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.Scope = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.Schema = "app_config";   // tables created in app_config.ConfigEntries, etc.
    b.UseSqlServer(connectionString);
});
```

| `Schema` value | SQL Server lands in | PostgreSQL lands in |
|---|---|---|
| `"configuration"` (default) | `configuration.ConfigEntries` | `configuration.config_entries` |
| `"my_custom"` | `my_custom.ConfigEntries` | `my_custom.config_entries` |
| `null` | `dbo.ConfigEntries` | `public.config_entries` |

The schema flows end-to-end: the SQL script's `CREATE SCHEMA` / `CREATE TABLE`
statements use it, the EF Core runtime model points the DbContext at the same place,
and the polling provider's queries hit the same tables. `Schema` is captured at
`AddDbConfig` time and locked in for the host's lifetime — change it across
deployments the same way you would change any schema layout (drop the old, recreate
the new, restart the host).

## `SchemaMode.CreateIfMissing` (default)

`AddDbConfig` runs the provider's SQL script synchronously before the polling
provider's first `Load()`. This matches Hangfire / Marten / Wolverine conventions:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.Scope = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
    // b.Options.SchemaMode = SchemaMode.CreateIfMissing; // already the default
});
```

The script is idempotent, so the call is safe to make on every startup. Pending DDL
applies on first run; subsequent runs are no-ops.

### Caveats

- **Multi-instance cold start.** All instances starting simultaneously will race on the
  `CREATE SCHEMA IF NOT EXISTS` / `CREATE TABLE IF NOT EXISTS` statements. These are
  themselves not atomic against concurrent DDL on some engines — in particular SQL Server
  may briefly report duplicate-object errors under high concurrency. The script tolerates
  this in practice because each statement is guarded, but if you have a documented
  problem with simultaneous cold starts, prefer the CI pre-deploy pattern below.
- **Database user permissions.** The connection string user needs `CREATE TABLE` and
  `CREATE SCHEMA` permissions for `CreateIfMissing`. If your prod database user is
  read/write-only on data, switch to `SchemaMode.None` and apply the schema via a
  privileged DBA path.
- **No automatic rollback.** Going forward from v0.13.0 is the only direction. Rolling
  back the schema requires manual `DROP TABLE` statements.

## `SchemaMode.None` — DBA-controlled schema

Set `SchemaMode.None` to skip the startup migrator entirely. Use this when DBAs or a
CI/CD pipeline owns the schema and applies it out of band:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.SchemaMode = SchemaMode.None;
    b.Options.Scope = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
});
```

Generate the SQL for offline application via the per-provider migrator helpers:

```csharp
using DbConfig.Provider.SqlServer;

// Get the script content with the schema substituted (no DB connection needed).
var sql = SqlServerDbConfigMigrator.GetCreateScript(schema: "configuration");
File.WriteAllText("dbconfig-create.sql", sql);

// Or apply it directly (idempotent — safe to call on every deploy).
await SqlServerDbConfigMigrator.MigrateAsync(connectionString, schema: "configuration");
```

For PostgreSQL, swap `SqlServerDbConfigMigrator` for `PostgreSqlDbConfigMigrator`.

### Provider migrator API

```csharp
public static class SqlServerDbConfigMigrator      // also: PostgreSqlDbConfigMigrator
{
    Task   MigrateAsync(string connectionString, string? schema = "configuration", CancellationToken ct = default);
    string GetCreateScript(string? schema = "configuration");
}
```

- **`MigrateAsync`** — opens a `DbConnection`, substitutes `{schema}`, and executes the
  embedded script. Idempotent.
- **`GetCreateScript`** — returns the script with `{schema}` substituted. Useful for
  generating offline DDL files. Does not touch the database.

## CI/CD pattern: apply migrations as a pre-deploy step

Most production teams prefer to make migrations a separate pre-deploy step rather than
relying on application startup:

```bash
# In a CI pre-deploy job
dotnet run --project tools/DbConfigMigrate -- --connection "$DB_URL"
```

`tools/DbConfigMigrate/Program.cs`:

```csharp
using DbConfig.Provider.SqlServer;

var connStr = args[Array.IndexOf(args, "--connection") + 1];
await SqlServerDbConfigMigrator.MigrateAsync(connStr);
Console.WriteLine("DbConfig schema applied.");
```

In the application host:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.SchemaMode = SchemaMode.None;
    // ... rest of config
});
```

Now production app instances skip migration entirely; they fail fast if the schema
hasn't been applied yet.

## Coexistence with your own migration tool

Many teams already use DbUp, FluentMigrator, or other migration tools for their primary
application schema. DbConfig's SQL script does not interfere with these.

Recommended pattern:

1. Your application migrations run first (creating your app tables).
2. DbConfig schema runs second — either auto via `SchemaMode.CreateIfMissing` or
   explicitly via `XxxDbConfigMigrator.MigrateAsync`.
3. Both tools operate on distinct tables (DbConfig's live in a separate schema by
   default).

## Adding new migrations (not needed by consumers)

Consumers never modify the DbConfig schema. If a future version of DbConfig adds a
table or column, the updated SQL script ships in the next release of the provider
package. Apply the new package and the next startup re-runs the idempotent script;
the new objects are created, existing ones are left alone.

For library maintainers: edit `Sql/InitialCreate.sql` directly. If a future version
needs to alter an existing table (rather than create a new one), the script's
idempotency guards must be extended to detect-and-add the new column / constraint
in-place, or a versioned migration mechanism must be introduced (out of scope for
v0.13.0).
