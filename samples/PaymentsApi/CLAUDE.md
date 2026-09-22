# PaymentsApi (sample) — Local Context

> This package lives in a monorepo. See root `AGENTS.md` for team-wide standards.
> This file only documents what's specific to this package.

## What this is

The demo host. Shows the unified `MapDbConfigAdmin` mount with built-in cookie login, backed by
`docker-compose.yml` for a local database.

## Local conventions

- **NOT production-grade auth.** `AppSettingsCredentialValidator` checks one shared password from
  `Auth:Password` in `appsettings.json`. Fine for a demo, wrong for a real deployment — never cite
  this file as the recommended pattern.
- `appsettings.json` and `appsettings.Development.json` are BOTH tracked in git — neither is ignored.
  Real credentials go in `appsettings.Development.Local.json` (matched by `**/appsettings.*.Local.json`)
  or environment variables.
- This is sample code that consumers read and copy. Prefer the clearest form over the cleverest,
  and keep it in sync with the composition patterns in `.claude/rules/architecture.md` §2.8.

## Dependencies / boundaries

- Consumes the packages the way an external consumer would — via the single-call
  `builder.AddDbConfig(...)`. Don't reach past the public API to make the sample work.
