# Testing (§4)

> Cite rules as §4.N. See `.claude/references/dotnet/testing-supplement.md`.

- **§4.1** `[ENFORCED]` Test framework is xUnit v3 on the Microsoft Testing Platform runner (`global.json` `runner: Microsoft.Testing.Platform`, `xunit.v3.mtp-v2`). Don't mix in MSTest/NUnit.
- **§4.2** `[ENFORCED]` Assertions use **Shouldly** (`.ShouldBe(...)`), not FluentAssertions — 84/84 test files use Shouldly. Match it in new tests. Mocking uses Moq.
- **§4.3** `[CONVENTION]` Verify relational behavior against real engines with Testcontainers (PostgreSQL + SQL Server) + Respawn for reset (`PostgreSqlFixture.cs`, `SqlServerFixture.cs`). Reserve `EntityFrameworkCore.InMemory` for behavior where relational semantics don't matter — do NOT default to it for query-translation/encoding/transaction tests.
- **§4.4** `[CONVENTION]` Control time via `TimeProvider` + `Microsoft.Extensions.TimeProvider.Testing`; don't rely on wall-clock.
- **§4.5** `[CONVENTION]` Test classes are `sealed`, named `{Subject}Tests`, organized by layer under `src/tests/DbConfig.Tests/{Core,EntityFrameworkCore,Http,Ui,PostgreSql,SqlServer,E2E}/`. Use `[Trait("Category", "...")]` to tag.
- **§4.6** `[CONVENTION]` Assertions must be meaningful — assert observable behavior (status codes, persisted rows, casing), not just "does not throw".
