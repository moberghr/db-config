---
paths:
  - "src/tests/**"
  - "ui/e2e/**"
axes:
  decision: process
  topic: testing
  scope: project
---

# Testing Standards

> 447 tests in `src/tests/DbConfig.Tests/` (xUnit v3 `xunit.v3.mtp-v2` + Shouldly + Moq + Respawn +
> Testcontainers MsSql/PostgreSql + `Microsoft.AspNetCore.TestHost`). Counts verified 2026-09-21 via
> `./DbConfig.Tests --list-tests`.

## Structure

- **§4.1** Tests are organised by **feature folder** (`Core/`, `Ui/`, `Http/`, …), not by a unit-vs-integration split.
- **§4.2** Dual-engine coverage is explicit, not generated. Every new behavior that can differ by engine — anything reaching the store, the schema, the migrators, or SQL generation — gets a SQL Server class AND a PostgreSQL class, each carrying the matching collection attribute:
  ```csharp
  [Collection(SqlServerFixture.CollectionName)]
  [Collection(PostgreSqlFixture.CollectionName)]
  ```
  There is no source generator in this repo — write both classes by hand. `[CONVENTION]`
- **§4.3** Test categories: `Unit` (no container, `InMemoryConfigStore`), `SqlServer`, `PostgreSql`, `E2E` (full host via `TestHost`, `EndToEndFixture` / `EndToEndPostgreSqlFixture` / `IncludeScopesE2EFixture`).

## Timing & Determinism

- **§4.4** Use `[TimedFact]` — 441 of 447 tests do. It carries a short default budget so deadlocks and hangs surface immediately; tests exercising genuinely slow behavior opt in explicitly with a larger budget. **NEVER raise the budget to fix a flake** — root-cause it.
- **§4.5** Production code uses `TimeProvider` (`data-layer.md` §5.6) so tests advance time instead of sleeping. Avoid `Task.Delay` in tests.
- **§4.6** Prefer deterministic reload triggers (`IDbConfigReloadSignal.TriggerReload()` or an explicit watermark advance) over waiting out a real polling interval.

## Test Construction

- **§4.7** Each test calls one public method on one class. Arrange via direct store/DB writes; use a fresh context for arrange / act / assert so no change tracking leaks between phases.
- **§4.8** Test naming: `MethodName_Scenario_ExpectedResult`.
- **§4.9** Use Shouldly: `entry.Value.ShouldBe("expected")`, `result.ShouldNotBeNull()`.
- **§4.10** Test code may use `DateTime.UtcNow` directly; production code may not.

## Running

- **§4.11** Run the test executable directly — `dotnet test` can exit 5 ("zero tests ran") on the MTP runner even when tests pass. See `project-specific.md` §8.5.
  ```bash
  cd src/tests/DbConfig.Tests/bin/Debug/net8.0
  ./DbConfig.Tests                                  # ./DbConfig.Tests.exe on Windows
  ./DbConfig.Tests --filter-trait "Category=Unit"
  ```
- **§4.12** Packages and tests multi-target — a green run on one TFM is not a green run on both. Check the framework you built against (`bin/Debug/net8.0` vs `bin/Debug/net10.0`).
- **§4.13** `ui/` has Playwright screenshot tests only (`ui/e2e/screenshots.spec.ts`, `npm run screenshots`) and no unit-test runner. UI logic that needs unit coverage belongs behind the HTTP API where the C# suite can reach it.
