# DbConfig.EntityFrameworkCore — Local Context

> This package lives in a monorepo. See root `AGENTS.md` for team-wide standards.
> This file only documents what's specific to this package.

## What this is

The shared EF Core layer both provider packages build on: `DbConfigDbContext`, `EfCoreConfigStore`,
the audit appender, the default encryptor, `IUniqueConstraintDetector`, and the single-call
`AddDbConfig` entry point.

## Local conventions

- **Marked `[TRANSITIVE — do not install directly]`** in its csproj `<Title>`/`<Description>`.
  Consumers install a provider package; this one arrives transitively.
- **No provider-specific exception inspection here.** `UpsertAsync` catches `DbUpdateException` and
  asks the injected `IUniqueConstraintDetector` whether it was a unique-constraint violation.
  `SqlException.Number` and `PostgresException.SqlState` belong in the provider packages.
- **Mutation + audit row commit in one `SaveChangesAsync`** (root `AGENTS.md` §0.5). Never split them.
- **There are no EF Core migrations anywhere in this repo.** Schema is created by hand-written
  `Sql/InitialCreate.sql` in each provider package, executed by that provider's migrator class.
  Changing the model means editing the entity here AND both providers' SQL scripts.

## Dependencies / boundaries

- References `DbConfig.Core` only. Never reference `Http`, `Ui`, or a provider package from here.
