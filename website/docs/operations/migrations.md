---
sidebar_position: 1
---

# Migrations

DbConfig manages its own database schema via EF Core migrations shipped inside the
provider packages. **The schema is applied automatically on host startup** by default —
you do not write the migrations, and you do not have to apply them manually.

This page covers the two `SchemaMode` options, the `DbConfigMigrator` static helper, and
the per-provider options helpers.

## Where migrations live

Each provider package ships its own migration set:

- `Moberg.DbConfig.Provider.SqlServer` — SQL Server migrations
- `Moberg.DbConfig.Provider.PostgreSql` — PostgreSQL migrations

The `DbConfigDbContext` lives in `Moberg.DbConfig.EntityFrameworkCore` and is shared
across both providers. EF Core finds the migrations via
`MigrationsAssembly("DbConfig.Provider.SqlServer")` (or PostgreSql), configured
internally by each provider's `Use*` extension. You never set `MigrationsAssembly`
yourself — the `SqlServerDbConfigOptions.ForSqlServer` / `PostgreSqlDbConfigOptions.ForPostgreSql`
helpers wrap that magic string when you need a standalone `DbContextOptions<DbConfigDbContext>`.

## Migrations shipped to date

| Migration name | What it does |
|----------------|-------------|
| `InitialCreate` | Creates `DbConfig_Entries` with composite unique constraint and polling index |
| `AddAuditEntries` | Creates `DbConfig_AuditEntries` table for the audit log |
| `CaseSensitiveScopeColumns` | Binary collation on `Scope`, `Environment`, and `Key` columns on both tables |
| `AddTenantIdColumn` | Adds `TenantId` column (case-sensitive) to both tables; widens the unique constraint to `(Scope, Environment, TenantId, Key)` |
| (UTC value converters) | `ModifiedUtc` columns are read as UTC `DateTime` / `DateTimeOffset` regardless of how the underlying engine stores them (defense-in-depth; no schema change) |

See [Releases](../releases.md) for which library version introduced each migration.

## `SchemaMode.CreateIfMissing` (default)

`AddDbConfig` invokes `Database.MigrateAsync` synchronously before the polling
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

EF Core's `__EFMigrationsHistory` table tracks state, so the call is idempotent. Pending
migrations apply on first run; subsequent runs are no-ops. Migrations run before
`Load()` so the polling provider never observes a partial schema.

### Caveats

- **Multi-instance cold start.** All instances starting simultaneously will try to take
  the migration lock; EF Core serializes them. The losing instances wait for the leader,
  then see a no-op. This is safe but adds ~1 s to startup for late joiners.
- **Database user permissions.** The connection string user needs `CREATE TABLE` and
  `ALTER TABLE` permissions for `CreateIfMissing`. If your prod database user is
  read/write-only on data, switch to `SchemaMode.None` and apply the schema via a
  privileged DBA path.
- **No rollback.** EF Core migrations are forward-only. Rolling back requires
  hand-written SQL.

## `SchemaMode.None` — DBA-controlled schema

Set `SchemaMode.None` to skip the startup migration entirely. Use this when DBAs or a
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

Generate the SQL for offline application via `DbConfigMigrator`. The per-provider
options helpers hide the `MigrationsAssembly` plumbing:

```csharp
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;

var opts = SqlServerDbConfigOptions.ForSqlServer(connectionString);

// One-shot create script (fresh database)
var createSql = DbConfigMigrator.GenerateCreateScript(opts);
File.WriteAllText("dbconfig-create.sql", createSql);

// Idempotent upgrade script (safe to re-apply)
var upgradeSql = DbConfigMigrator.GenerateMigrationScript(opts, idempotent: true);
File.WriteAllText("dbconfig-upgrade.sql", upgradeSql);
```

For PostgreSQL, swap `SqlServerDbConfigOptions.ForSqlServer` for
`PostgreSqlDbConfigOptions.ForPostgreSql`.

### `DbConfigMigrator` API surface

```csharp
public static class DbConfigMigrator
{
    Task MigrateAsync(DbContextOptions<DbConfigDbContext> options, CancellationToken ct = default);
    string GenerateCreateScript(DbContextOptions<DbConfigDbContext> options);
    string GenerateMigrationScript(
        DbContextOptions<DbConfigDbContext> options,
        string? fromMigration = null,
        string? toMigration = null,
        bool idempotent = true);
}
```

- **`MigrateAsync`** — apply pending migrations programmatically. Equivalent to
  `ctx.Database.MigrateAsync()` but doesn't require constructing the context yourself.
- **`GenerateCreateScript`** — full DDL for a fresh database. Use for greenfield setups.
- **`GenerateMigrationScript`** — incremental upgrade SQL. With `idempotent: true`
  (default), the script checks `__EFMigrationsHistory` before each step so it can be
  re-run safely.

## CI/CD pattern: apply migrations as a pre-deploy step

Most production teams prefer to make migrations a separate pre-deploy step rather than
relying on application startup:

```bash
# In a CI pre-deploy job
dotnet run --project tools/DbConfigMigrate -- --connection "$DB_URL"
```

`tools/DbConfigMigrate/Program.cs`:

```csharp
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;

var connStr = args[Array.IndexOf(args, "--connection") + 1];
var opts = SqlServerDbConfigOptions.ForSqlServer(connStr);
await DbConfigMigrator.MigrateAsync(opts);
Console.WriteLine("DbConfig migrations applied.");
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

## The `CaseSensitiveScopeColumns` migration

The collation migration changes the collation on scope columns in both tables:

- **SQL Server:** `Latin1_General_100_BIN2` (binary, case-sensitive)
- **PostgreSQL:** `"C"` (byte-level, case-sensitive)

:::warning
This migration acquires a schema lock (`Sch-M` on SQL Server, `AccessExclusiveLock` on
PostgreSQL) on both tables for the duration of the `ALTER` statement. SQL Server does
not support `ONLINE = ON` for collation changes.

For tables under live read load, quiesce the host (stop taking traffic) or schedule the
migration during a maintenance window before applying.
:::

After this migration, `"MyApp"` and `"myapp"` are distinct scope names. Ensure all your
`AddDbConfig` registrations and `scopeFilter` values use consistent casing.

## Coexistence with your own migration tool

Many teams already use DbUp, FluentMigrator, or other migration tools for their primary
application schema. DbConfig's EF migrations do not interfere with these.

Recommended pattern:

1. Your application migrations run first (creating your app tables).
2. DbConfig migrations run second — either auto via `SchemaMode.CreateIfMissing` or
   manually via `DbConfigMigrator.MigrateAsync`.
3. Both tools operate on distinct tables (`DbConfig_*` is name-spaced).
4. EF Core's `__EFMigrationsHistory` records DbConfig migrations; your tool maintains
   its own history table.

## Adding new migrations (not needed by consumers)

Consumers never add migrations to the DbConfig schema. If a future version of DbConfig
adds a table or column, a new migration ships in the updated provider package. Apply it
the same way you applied the initial migrations.

Only the DbConfig package maintainers run `dotnet ef migrations add` against the
provider projects.
