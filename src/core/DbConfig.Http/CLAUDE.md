# DbConfig.Http — Local Context

> This package lives in a monorepo. See root `AGENTS.md` for team-wide standards.
> This file only documents what's specific to this package.

## What this is

The JSON CRUD surface over config entries: `MapDbConfigHttp` (returns a `RouteGroupBuilder`), the
endpoint handlers, and the auth contracts `IDbConfigAuthorizationFilter` /
`IDbConfigCredentialValidator`.

## Local conventions

- **The auth contracts live here, not in `Ui`** — they are shared by the unified admin mount, the
  UI's built-in login, and consumers attaching a filter to `MapDbConfigHttp` without taking a UI
  dependency.
- **Ships no `[Authorize]` attributes, no policies, no authentication middleware.** The host owns
  identity. Composition patterns are in `.claude/rules/architecture.md` §2.8.
- Write endpoints (`PUT`, `DELETE`, `POST /reload`) must call `IDbConfigReloadSignal.TriggerReload()`
  after mutating, so in-process consumers don't wait out the polling interval.
- `scopeFilter` is enforced at the group level — a write outside it returns 403 (root `AGENTS.md` §0.4).
- GET responses always return plaintext; the store decrypts before the HTTP layer sees a value.

## Dependencies / boundaries

- References `DbConfig.Core` only — never EF Core or a provider package.
