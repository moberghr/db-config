# DbConfig — Product Context

## Purpose

A database-backed `IConfiguration` provider for .NET with an embedded React editor UI, so teams
change application configuration at runtime without a redeploy.

## Users

- **Application developers** — add `builder.AddDbConfig(...)` and read config through the standard
  `IConfiguration` / `IOptionsSnapshot<T>` pipeline. They should never need to learn a new API.
- **Operations / platform teams** — edit entries through the React admin UI or the HTTP API,
  and read the audit trail of who changed what.
- **Platform owners of shared config** — own `Shared` / `PlatformDefaults` scopes that many
  applications include and override.

## Key Flows

1. **Read** — `IConfiguration[key]` → `DbConfigConfigurationProvider.TryGet` → in-memory dictionary,
   resolving tenant → scope → global precedence.
2. **Reload** — a polling loop compares a `MAX(ModifiedUtc)` watermark on a configurable interval;
   HTTP writes additionally fire `IDbConfigReloadSignal` for immediate in-process refresh.
3. **Edit** — React UI → `Moberg.DbConfig.Http` JSON endpoints → `IConfigStore` → EF Core, with an
   audit row committed in the same transaction.
4. **Protect** — entries flagged `IsSecret` are encrypted at rest via `IConfigEncryptor`
   (ASP.NET Data Protection by default) and masked in the UI.

## Non-Goals

- **Not an identity or authorization system.** The package ships no `[Authorize]` attributes and no
  authentication middleware; the host owns identity entirely.
- **Not a secrets manager.** `IsSecret` is at-rest encryption plus UI masking, not an access
  boundary — every app whose host includes a scope can read every byte of it in-process.
- **Not a feature-flag platform.** No targeting rules, percentage rollouts, or evaluation SDKs.
- **Not a first-class target for direct SQL mutation.** Writes that bypass the API skip the reload
  signal and the audit log.
- **Not tuned for very large tenant counts.** All tenants load eagerly; ~10K tenants × 100 keys is
  the documented ceiling.
