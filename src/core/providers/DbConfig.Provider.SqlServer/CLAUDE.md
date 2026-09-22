# DbConfig.Provider.SqlServer — Local Context

> This package lives in a monorepo. See root `AGENTS.md` for team-wide standards.
> This file only documents what's specific to this package.

## What this is

The SQL Server provider: the `UseSqlServer` builder extension, `SqlServerUniqueConstraintDetector`, and the
schema migrator that executes `Sql/InitialCreate.sql`.

## Local conventions

- **This is one of the only places provider-native SQL is allowed** (`data-layer.md` §5.1).
  `SqlServerDbConfigMigrator` runs `Sql/InitialCreate.sql` via `DbCommand.CommandText` /
  `ExecuteNonQueryAsync` — there are no EF Core migrations, no `Designer.cs`, no `ModelSnapshot.cs`.
- **Schema changes require editing BOTH providers' SQL scripts**, not just this one, plus the entity
  in `DbConfig.EntityFrameworkCore`. Tables here are [ConfigEntries] / [AuditEntries].
- `SqlServerUniqueConstraintDetector` owns the engine-specific check (SqlException.Number 2627/2601). Keep that
  knowledge here — `EfCoreConfigStore` must stay provider-agnostic.
- Any behavior added here needs a matching test on the other engine too (`testing.md` §4.2).

## Dependencies / boundaries

- References `DbConfig.EntityFrameworkCore` (and `Core` transitively). Never referenced by `Core`,
  `Http`, or `Ui`.
