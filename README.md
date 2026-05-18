# DbConfig

Database-backed `IConfiguration` provider for .NET with an embedded React editor UI.

[![NuGet](https://img.shields.io/nuget/v/Moberg.DbConfig.Core?label=DbConfig.Core)](https://www.nuget.org/packages/Moberg.DbConfig.Core)
[![NuGet](https://img.shields.io/nuget/v/Moberg.DbConfig.Http?label=DbConfig.Http)](https://www.nuget.org/packages/Moberg.DbConfig.Http)
[![NuGet](https://img.shields.io/nuget/v/Moberg.DbConfig.Ui?label=DbConfig.Ui)](https://www.nuget.org/packages/Moberg.DbConfig.Ui)
[![NuGet](https://img.shields.io/nuget/v/Moberg.DbConfig.Provider.SqlServer?label=Provider.SqlServer)](https://www.nuget.org/packages/Moberg.DbConfig.Provider.SqlServer)
[![NuGet](https://img.shields.io/nuget/v/Moberg.DbConfig.Provider.PostgreSql?label=Provider.PostgreSql)](https://www.nuget.org/packages/Moberg.DbConfig.Provider.PostgreSql)
[![Docs](https://img.shields.io/badge/docs-moberghr.github.io%2Fdb--config-blue)](https://moberghr.github.io/db-config/)

> Mimics the ergonomics of a secrets manager, but persists configuration in your
> existing application database. No additional external service required.

## Packages

| Package | Purpose |
|---|---|
| `Moberg.DbConfig.Core` | `IConfigurationSource` / `IConfigurationProvider`, `IConfigStore` abstraction, options |
| `Moberg.DbConfig.Http` | JSON API endpoints (`MapDbConfigHttp`), host-owned authorization |
| `Moberg.DbConfig.Ui` | React editor UI shipped as embedded static assets (`MapDbConfigUi`) |
| `Moberg.DbConfig.Provider.SqlServer` | SQL Server EF Core provider + dialect specifics |
| `Moberg.DbConfig.Provider.PostgreSql` | PostgreSQL (Npgsql) EF Core provider + dialect specifics |

## Security

Per-entry encryption via `IsSecret` flag:
- Mark sensitive entries (`IsSecret = true`) → encrypted at rest using ASP.NET Core
  Data Protection (default). Non-secret values stay plaintext for debuggability.
- The default Data Protection key ring is ephemeral and process-scoped. For multi-instance
  or restart-stable deployments, persist keys BEFORE `AddDbConfig`:

  ```csharp
  builder.Services.AddDataProtection()
      .PersistKeysToFileSystem(new DirectoryInfo("/var/dbconfig/keys"))
      .ProtectKeysWithCertificate("thumbprint");
  builder.AddDbConfig(b => { ... });
  ```
- Custom key management (Azure Key Vault, AWS KMS, etc.): register a custom
  `IConfigEncryptor` in `builder.Services` BEFORE `AddDbConfig`. Both instance and
  type-mapped registrations work in v0.6.0:

  ```csharp
  // Type-mapped (DI resolves dependencies)
  builder.Services.AddSingleton<IConfigEncryptor, MyAzureKeyVaultEncryptor>();
  builder.AddDbConfig(b => { ... });

  // Or instance-registered (when you already have an instance)
  builder.Services.AddSingleton<IConfigEncryptor>(myEncryptorInstance);
  builder.AddDbConfig(b => { ... });
  ```

  **Type-mapped caveat:** the polling provider's decryption is deferred until host
  construction completes (an `IHostedService` resolves the encryptor and activates
  decryption). Reading secret config values BEFORE `host.RunAsync()` (or
  `host.StartAsync()`) is unsupported and throws a clear `InvalidOperationException`.
  Reading non-secret values pre-build is unaffected. Most code reads config from
  request handlers or hosted services, which run after build, so this is rarely
  hit in practice.
- Non-secret values are NOT encrypted by design — feature flags, polling intervals, log
  levels stay plaintext for `psql` / SSMS debugging convenience.

## Getting started

```csharp
// 1. Single call — wires services, configuration source, and reload signal
builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(30);
    b.UseSqlServer(connectionString); // or b.UsePostgreSql(connectionString)
});

// 2. Map the API + UI
app.MapDbConfigHttp("/api/dbconfig").RequireAuthorization("DbConfigAdmin");
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");
```

`AddDbConfig` is an extension on `IHostApplicationBuilder`, so it works for both
`WebApplicationBuilder` (ASP.NET Core) and `HostApplicationBuilder` (worker
services / generic host).

The connection string must be present before `AddDbConfig` is called. If it is missing, an
`InvalidOperationException` is thrown at startup — the provider does not silently return empty
values.

See `src/demo/DbConfig.Demo.WebApp/` for a full working example including migrations, an
API-key auth handler, and user-secrets setup instructions.

### Shared scopes

To pull configuration from one or more shared scopes in addition to your app's own:

```csharp
builder.AddDbConfig(b =>
{
    b.UseSqlServer(connectionString);
    b.Options.AppName = "PaymentService";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.IncludeScopes = ["PlatformDefaults", "Shared"];
    // Precedence (lowest → highest): PlatformDefaults < Shared < PaymentService
    // Own scope (AppName) always wins ties.
});
```

The polling provider reads from all listed scopes in one DB query and merges them with the
configured precedence. A change in any included scope advances the watermark and triggers
reload across all consumers within one poll interval.

**Per-scope authorization (host pattern):**

```csharp
// App-team writes — only own scope
app.MapDbConfigHttp("/api/dbconfig", scopeFilter: "PaymentService")
   .RequireAuthorization("AppTeamAdmin");

// Platform-team writes — only Shared scope
app.MapDbConfigHttp("/api/dbconfig-shared", scopeFilter: "Shared")
   .RequireAuthorization("PlatformAdmin");
```

When `scopeFilter` is set, the group rejects writes (and reads) to other AppNames with 403.
The `/reload` endpoint is always allowed.

### Audit log

Every mutation (Upsert/Delete) writes a row to `DbConfig_AuditEntries` in the same
transaction. The UI's per-row "History" button surfaces this; programmatic access via
`GET /{appName}/{environment}/audit/{*key}?take=50` returns `ConfigAuditEntry[]`.

Audit log values are encrypted-at-rest using the same `IConfigEncryptor` as the main
store. The history endpoint decrypts for the response, so callers see plaintext.

Disable per-host with `b.Options.EnableAuditLog = false`. Retention is the consumer's
responsibility — recommended `DELETE FROM DbConfig_AuditEntries WHERE ModifiedUtc < NOW() - INTERVAL '90 days'`
on a schedule.

### Read auditing (opt-in)

By default DbConfig audits only mutations (Insert/Update/Delete). For compliance scenarios
that require "who read this secret?" trails, enable read auditing:

```csharp
builder.AddDbConfig(b =>
{
    b.UseSqlServer(connStr);
    b.Options.AppName = "PaymentService";
    b.Options.AuditReads = true;   // NEW in v0.6.0
});
```

When enabled, HTTP `GET /{app}/{env}` and `GET /{app}/{env}/{*key}` write fire-and-forget
audit rows with `Action=Read`. Old/New values are null (the read itself isn't a state change).
Failures to write the audit row log a warning; the GET still returns successfully.

Read audit rows are written for both 200 and 404 responses — a key probe is recorded even
when the key doesn't exist. This is intentional for compliance posture (record access
attempts, not just successful accesses).

The audit-history endpoint never generates read audits (no recursion).

## Reload semantics

The configuration provider polls the store on a configurable interval (default 30 s). When
the highest-watermark `ModifiedUtc` in the store advances, the provider fires an
`IChangeToken`, which triggers `IOptionsMonitor` callbacks in the consuming application.

**Important:** Direct SQL `DELETE` on the `DbConfig_Entries` table will not be reflected by
the polling provider until another row's `ModifiedUtc` advances. Always mutate via the API —
the HTTP `DELETE`/`PUT` endpoints fire the in-process reload signal. Direct DB writes from
migrations or DBA tools are not first-class in v0.1.0.

The HTTP `POST /reload` endpoint (mapped by `MapDbConfigHttp`) triggers an immediate in-process
reload without waiting for the next poll interval.

## Theming

The UI editor supports light and dark themes. Toggle via the sun/moon button in the page
header; choice persists to `localStorage`. The Docusaurus docs site has its own light/dark
toggle (top-right navbar). See [`website/docs/ui-editor/theming.md`](./website/docs/ui-editor/theming.md)
for implementation details.

## Documentation

Full documentation lives under [`website/`](./website/) (Docusaurus 3.10). To browse locally:

```bash
cd website
npm install
npm run start         # http://localhost:3000
```

Or build static HTML:

```bash
cd website
npm run build
npm run serve         # serve build/ on http://localhost:3000
```

UI screenshots in the docs are produced by a Playwright suite against a deterministic
demo-mode of the UI. To regenerate them:

```bash
cd ui
npm run screenshots:install   # one-time: playwright install chromium
npm run screenshots           # produces 10 PNGs in website/static/img/screenshots/
```

The screenshots cover all v0.x features: entries list, editing, history with diff, bulk
operations, import/export, scope selector, and the access warning banner.

## Status

- **v0.6.0 (2026-05-17):** Opt-in read auditing; UI features (diff view, bulk edit,
  import/export); type-mapped `IConfigEncryptor` registrations.

v0.5.0 (production hardening) — production-ready for the following scope:

- SQL Server and PostgreSQL via EF Core
- Hierarchical keys, App + Environment scoping
- Polling-based reload with immediate-reload signal
- Embedded React editor UI with CRUD, secret masking, scope badge, view-mode toggle, and per-row audit history
- Host-owned authorization (no auth baked into the package)
- `Moberg.DbConfig.EntityFrameworkCore` extracted from Core — consumers writing custom
  non-EF stores no longer pull the EF transitive dependency
- `IUniqueConstraintDetector` strategy — provider-specific exception handling lives in the
  provider package, not in the shared store
- Single-call design — `builder.AddDbConfig(b => ...)` on `IHostApplicationBuilder`
  wires services, configuration source, and reload signal in one shot. No bridge
  dance, no second DI container.
- `IConfigStore.GetAsync` for targeted single-key reads — HTTP GET single no longer scans
  the full app/environment scope
- `DbConfigOptions.IncludeScopes` — pull config from one or more shared scopes in addition
  to your own, with explicit precedence ordering
- `MapDbConfigHttp(scopeFilter: "X")` — per-scope authorization at the group level
- Per-entry encryption via `IConfigEncryptor` (`IsSecret = true` → encrypted at rest via ASP.NET Core Data Protection)
- Audit log (`DbConfig_AuditEntries`) with in-transaction writes, HTTP read endpoint, and UI History dialog
- Case-sensitive binary collation on scope columns (closes collation mismatch between HTTP filter and DB query)

**Known limitations:**

- No audit log retention pruner (manual cleanup documented; opt-in pruner deferred to v0.7.0+)
- Direct DB mutations bypass the reload signal AND audit log (always mutate via API — see Reload semantics above)
- Two `EfCoreConfigStore` instances per host (one for the polling provider, one
  for the HTTP layer) both pointed at the same DB. They share no in-process state
  by design — the DB is the source of truth and the reload signal coordinates
  cache invalidation. Custom `IConfigStore` impls (e.g. Redis) currently can't
  share a single instance across polling and HTTP; a `UseCustomStore<T>()`
  registration helper is tracked for v0.6.0.
- Ephemeral Data Protection key ring by default (document `PersistKeysToXxx` — see Security section)
