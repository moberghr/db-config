---
sidebar_position: 1
---

# Migrations

DbConfig manages its own database schema via EF Core migrations shipped inside the provider
packages. You do not write or maintain these migrations — you just apply them.

## Where migrations live

Each provider package ships its own migration set:

- `Moberg.DbConfig.Provider.SqlServer` — SQL Server migrations
- `Moberg.DbConfig.Provider.PostgreSql` — PostgreSQL migrations

The `DbConfigDbContext` lives in `Moberg.DbConfig.EntityFrameworkCore` and is shared across
both providers. EF Core finds the migrations via `MigrationsAssembly("DbConfig.Provider.SqlServer")`
(or PostgreSql), configured internally by each provider's `Use*` extension. You do not
need to set `MigrationsAssembly` yourself.

## Migrations shipped in v0.x

| Migration name | Version | What it does |
|----------------|---------|-------------|
| `InitialCreate` | v0.1.0 | Creates `DbConfig_Entries` with composite unique constraint and polling index |
| `AddAuditEntries` | v0.5.0 | Creates `DbConfig_AuditEntries` table for the audit log |
| `CaseSensitiveScopeColumns` | v0.5.0 | Applies binary collation to `AppName`, `Environment`, and `Key` columns on both tables |

## Applying migrations

### Option A — CLI (recommended for CI/CD)

```bash
dotnet ef database update \
  --project src/YourApp/YourApp.csproj \
  --startup-project src/YourApp/YourApp.csproj \
  --context DbConfigDbContext
```

The EF CLI resolves `DbConfigDbContext` from the startup project's DI (via
`IDesignTimeDbContextFactory` or the host factory). Make sure `AddDbConfig` is called in
the startup project's `Program.cs`.

### Option B — Programmatic (demos and development)

```csharp
// Program.cs — after builder.Build(), before app.Run()
var migrateOptions = new DbContextOptionsBuilder<DbConfigDbContext>()
    .UseSqlServer(
        connectionString,
        sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer"))
    .Options;

await using var ctx = new DbConfigDbContext(migrateOptions);
await ctx.Database.MigrateAsync();
```

For PostgreSQL:

```csharp
var migrateOptions = new DbContextOptionsBuilder<DbConfigDbContext>()
    .UseNpgsql(
        connectionString,
        pg => pg.MigrationsAssembly("DbConfig.Provider.PostgreSql"))
    .Options;

await using var ctx = new DbConfigDbContext(migrateOptions);
await ctx.Database.MigrateAsync();
```

:::warning
Applying migrations programmatically on every startup can cause schema-lock contention in
multi-instance deployments. Use it for development and demos; prefer the CLI for production.
:::

## The `CaseSensitiveScopeColumns` migration

This migration changes the collation on `AppName`, `Environment`, and `Key` columns in both
`DbConfig_Entries` and `DbConfig_AuditEntries`:

- **SQL Server:** `Latin1_General_100_BIN2` (binary, case-sensitive)
- **PostgreSQL:** `"C"` (byte-level, case-sensitive)

:::warning
The `CaseSensitiveScopeColumns` migration acquires a schema lock (`Sch-M` on SQL Server,
`AccessExclusiveLock` on PostgreSQL) on both tables for the duration of the `ALTER` statement.
SQL Server does not support `ONLINE = ON` for collation changes.

For tables under live read load, quiesce the host (stop taking traffic) or schedule the
migration during a maintenance window before applying.
:::

After this migration, `"MyApp"` and `"myapp"` are distinct scope names. Ensure all your
`AddDbConfig` registrations and `scopeFilter` values use consistent casing.

## Coexistence with your own migration tool

Many teams already use DbUp, FluentMigrator, or other migration tools for their primary
application schema. DbConfig's EF migrations do not interfere with these.

Recommended pattern:

1. Apply DbConfig migrations at startup (programmatic) or as a pre-deploy step (CLI).
   DbConfig's migration history is tracked in `__EFMigrationsHistory` with a specific
   migration context name.
2. Your own migration tool runs separately (either before or after) and manages its own
   history table.
3. Both tools operate on distinct tables and do not conflict.

## Adding new migrations (not needed by consumers)

Consumers never add migrations to the DbConfig schema. If a future version of DbConfig adds
a table or column, a new migration will be included in the updated provider package. Apply
it the same way you applied the initial migrations.

Only the DbConfig package maintainers run `dotnet ef migrations add` against the provider
projects.
