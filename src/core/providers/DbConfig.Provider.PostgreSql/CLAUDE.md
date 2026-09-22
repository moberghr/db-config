# DbConfig.Provider.PostgreSql — Local Context

> This package lives in a monorepo. See root `AGENTS.md` for team-wide standards.
> This file only documents what's specific to this package.

## What this is

The PostgreSQL provider: the `UsePostgreSql` builder extension, `PostgreSqlUniqueConstraintDetector`, and the
schema migrator that executes `Sql/InitialCreate.sql`.

## Local conventions

- **This is one of the only places provider-native SQL is allowed** (`data-layer.md` §5.1).
  `PostgreSqlDbConfigMigrator` runs `Sql/InitialCreate.sql` via `DbCommand.CommandText` /
  `ExecuteNonQueryAsync` — there are no EF Core migrations, no `Designer.cs`, no `ModelSnapshot.cs`.
- **Schema changes require editing BOTH providers' SQL scripts**, not just this one, plus the entity
  in `DbConfig.EntityFrameworkCore`. Tables here are "config_entries" / "audit_entries" (snake_case).
- `PostgreSqlUniqueConstraintDetector` owns the engine-specific check (PostgresException.SqlState "23505"). Keep that
  knowledge here — `EfCoreConfigStore` must stay provider-agnostic.
- Any behavior added here needs a matching test on the other engine too (`testing.md` §4.2).

## Dependencies / boundaries

- References `DbConfig.EntityFrameworkCore` (and `Core` transitively). Never referenced by `Core`,
  `Http`, or `Ui`.
