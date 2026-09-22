# DbConfig.Tests — Local Context

> This package lives in a monorepo. See root `AGENTS.md` for team-wide standards.
> This file only documents what's specific to this package.

## What this is

The whole suite — 447 tests across unit, SQL Server, PostgreSQL, and end-to-end host fixtures.

## Build / test (local)

Run the executable directly; `dotnet test` can exit 5 on the xUnit v3 MTP runner even when tests pass:

```bash
cd bin/Debug/net8.0          # or net10.0 — packages multi-target
./DbConfig.Tests                                  # ./DbConfig.Tests.exe on Windows
./DbConfig.Tests --filter-trait "Category=Unit"
```

## Local conventions

- Store-touching behavior needs a SQL Server class AND a PostgreSQL class — written by hand, using
  `[Collection(SqlServerFixture.CollectionName)]` / `[Collection(PostgreSqlFixture.CollectionName)]`.
  There is no source generator in this repo.
- `[TimedFact]` is the default attribute (441 of 447 tests). Never raise a budget to fix a flake.
- Test code may use `DateTime.UtcNow`; production code uses `TimeProvider`.

## Dependencies / boundaries

- SQL Server and PostgreSQL containers come from Testcontainers — a run needs a working Docker daemon.
