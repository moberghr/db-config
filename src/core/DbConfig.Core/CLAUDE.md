# DbConfig.Core — Local Context

> This package lives in a monorepo. See root `AGENTS.md` for team-wide standards.
> This file only documents what's specific to this package.

## What this is

The provider-agnostic core: `IConfigStore`, `ConfigEntry`, `DbConfigOptions`, `DbConfigBuilder`,
`DbConfigConfigurationSource`/`Provider`, `IDbConfigReloadSignal`, `ITenantResolver`, and the
test-only `InMemoryConfigStore`.

## Local conventions

- **This package must not reference any other db-config package.** Actual `ProjectReference` graph:
  `Http → Core`; `Ui → Core + Http`; `EntityFrameworkCore → Core`; `Provider.{SqlServer,PostgreSql} →
  EntityFrameworkCore`. Note `Http` and `Ui` do NOT depend on `EntityFrameworkCore`. Every arrow
  points at Core, never out of it — a circular reference is a build error.
- **No EF Core dependency here, ever.** `DbConfigDbContext` and `EfCoreConfigStore` live in
  `DbConfig.EntityFrameworkCore` specifically so `Core` doesn't drag
  `EntityFrameworkCore.Relational` onto consumers writing custom non-EF stores.
- `InMemoryConfigStore` is a test helper: in-process only, no persistence, no reload coordination
  across hosts. (It does lock internally, so the limitation is durability and scope, not raw thread
  safety.) Do not promote it to a supported store.
- `DbConfigConfigurationProvider.TryGet` is the hot read path. Tenant → scope → global fallback
  lives here, never in a store implementation.

## Dependencies / boundaries

- Public API surface of a published NuGet package — keep the XML doc comments, and treat any
  signature change as a versioning decision.
