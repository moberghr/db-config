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
| `Moberg.DbConfig.Http` | JSON API endpoints (`MapDbConfigHttp`); auth is host-owned via `RequireAuthorization` |
| `Moberg.DbConfig.Ui` | React editor UI shipped as embedded static assets (`MapDbConfigUi`); optional built-in cookie login |
| `Moberg.DbConfig.Provider.SqlServer` | SQL Server EF Core provider + dialect specifics |
| `Moberg.DbConfig.Provider.PostgreSql` | PostgreSQL (Npgsql) EF Core provider + dialect specifics |

## Try it out

A runnable sample app — multi-tenant payments processor — lives at
[`samples/PaymentsApi/`](samples/PaymentsApi/). Demonstrates per-tenant
config overrides, `IOptionsSnapshot<T>` binding, at-rest encryption,
audit log, and live reload via the embedded admin UI.

```bash
cd samples/PaymentsApi
docker compose up -d
dotnet run
```

Then open `http://localhost:5000/admin/dbconfig` — the UI loads all your
entries immediately and the filter fields in the toolbar narrow by
Scope, Environment, or Tenant.

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
  type-mapped registrations are supported:

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
    b.Options.Scope = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(30);
    b.UseSqlServer(connectionString); // or b.UsePostgreSql(connectionString)
});

// 2. Map the admin surface (UI + API under one prefix, one cookie)
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();
app.MapDbConfigAdmin("/admin/dbconfig", opts =>
{
    opts.UseBuiltInLogin<MyValidator>();
});
// → UI at  /admin/dbconfig
// → API at /admin/dbconfig/api
```

`AddDbConfig` is an extension on `IHostApplicationBuilder`, so it works for both
`WebApplicationBuilder` (ASP.NET Core) and `HostApplicationBuilder` (worker
services / generic host).

The connection string must be present before `AddDbConfig` is called. If it is missing, an
`InvalidOperationException` is thrown at startup — the provider does not silently return empty
values.

See [`samples/PaymentsApi/`](samples/PaymentsApi/) for a full working example —
multi-tenant payments processor demonstrating per-tenant overrides,
`IOptionsSnapshot<T>` binding, at-rest encryption, audit log, and live reload
via the embedded admin UI.

### Authentication

`MapDbConfigAdmin`, `MapDbConfigHttp`, and `MapDbConfigUi` are all open by
default and return `RouteGroupBuilder`. Five supported patterns:

| Pattern | When |
|---|---|
| `MapDbConfigAdmin(prefix, opts => opts.UseBuiltInLogin<T>())` | Recommended — one cookie covers UI + API |
| Open (no auth) | Private network, dev only |
| `.RequireAuthorization("policy")` | The host already has OIDC / Windows Auth / JWT |
| Split prefixes + `opts.UseBuiltInLogin<T>()` | UI behind CDN, API at different origin |
| `opts.Authorization = new MyFilter()` | Header-based, IP allowlist, custom JWT cookie |

The unified `MapDbConfigAdmin` is the common case — one call mounts UI and HTTP
API under one prefix with one shared cookie, so the React app can call its own
backend right after sign-in.

See [Authentication & authorization](https://moberg.github.io/db-config/configuration/auth)
for the full walkthrough.

### Programmatic access

Most code reads config through `IOptionsSnapshot<T>` (tenant-aware automatically) or
`IConfiguration`. For admin endpoints, background jobs, or diagnostic surfaces that need to
read **another tenant's settings** explicitly, two services are available:

```csharp
// Typed bind via the standard pipeline. Uses the SAME section path you
// registered for IOptionsSnapshot<T>; no new convention to learn.
public class AdminController(ITenantConfigReader reader)
{
    public IActionResult ShowAcmeStripe()
    {
        // Reuses `services.Configure<StripeSettings>(config.GetSection("Stripe"))`
        // — picks Acme's entries with global fallback, runs PostConfigure delegates.
        var stripe = reader.GetForTenant<StripeSettings>("Acme");
        return Ok(stripe);
    }
}

// Direct DB read with raw entries (IsSecret, ModifiedUtc, ModifiedBy).
// Bypasses IConfiguration; section name is typeof(T).Name verbatim.
public class AuditEndpoint(IConfigStore store)
{
    public async Task<IActionResult> GetEntry(CancellationToken ct)
    {
        var entry = await store.GetForTenantAsync("Acme", "Stripe:ApiKey", ct);
        return Ok(new { entry?.Value, entry?.IsSecret, entry?.ModifiedUtc });
    }
}
```

`ITenantConfigReader` is the right tool for typed-POCO reads in application code. It
respects every `services.Configure<T>(...)` registration including custom section paths
and `PostConfigure` delegates — internally it sets an `AsyncLocal` tenant override on
the polling provider and resolves `IOptionsSnapshot<T>` in a fresh DI scope.

`IConfigStore` is the right tool for admin tooling that needs raw entries with metadata
(`IsSecret`, `ModifiedUtc`, `ModifiedBy`) or for binding a type that has no
`IConfigureOptions<T>` registration. Its typed-bind overloads use `typeof(T).Name`
verbatim — `StripeOptions` reads `StripeOptions:` keys.

See [Programmatic access to configuration](https://moberg.github.io/db-config/configuration/programmatic-access)
for the full walkthrough.

### Migrations

DbConfig owns its own schema (tables `DbConfig_Entries` and `DbConfig_AuditEntries`).

#### Default — auto-create on startup

The library applies pending migrations during `AddDbConfig`. No extra code needed:

```csharp
builder.AddDbConfig(b =>
{
    b.UseSqlServer(connStr);
    b.Options.Scope = "MyApp";
    // b.Options.SchemaMode = SchemaMode.CreateIfMissing;  // default
});
```

This mirrors how Hangfire, Marten, and Wolverine handle schema. Good for dev, demos,
and small-team production.

#### DBA-controlled / CI-pipeline workflows

Production teams that prefer to apply schema out of band (via init container, SQL
review by DBA, CI/CD step) can disable auto-create:

```csharp
builder.AddDbConfig(b =>
{
    b.UseSqlServer(connStr);
    b.Options.Scope = "MyApp";
    b.Options.SchemaMode = SchemaMode.None;  // host assumes schema is ready
});
```

To extract SQL for offline application:

```csharp
// In a tiny build-time helper console app:
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;

var opts = SqlServerDbConfigOptions.ForSqlServer(connStr);
var sql = DbConfigMigrator.GenerateMigrationScript(opts, idempotent: true);
File.WriteAllText("dbconfig-upgrade.sql", sql);
```

`DbConfigMigrator` exposes three methods:

- `MigrateAsync(opts)` — apply pending migrations programmatically
- `GenerateCreateScript(opts)` — full schema DDL for a fresh database
- `GenerateMigrationScript(opts, fromMigration?, toMigration?, idempotent: true)` — incremental upgrade SQL

For PostgreSQL hosts use `PostgreSqlDbConfigOptions.ForPostgreSql(connStr)`.

### Shared scopes

To pull configuration from one or more shared scopes in addition to your app's own:

```csharp
builder.AddDbConfig(b =>
{
    b.UseSqlServer(connectionString);
    b.Options.Scope = "PaymentService";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.IncludeScopes = ["PlatformDefaults", "Shared"];
    // Precedence (lowest → highest): PlatformDefaults < Shared < PaymentService
    // Own scope (Scope) always wins ties.
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

When `scopeFilter` is set, the group rejects writes (and reads) to other Scopes with 403.
The `/reload` endpoint is always allowed.

### Audit log

Every mutation (Upsert/Delete) writes a row to `DbConfig_AuditEntries` in the same
transaction. The UI's per-row "History" button surfaces this; programmatic access via
`GET /{scope}/{environment}/audit/{*key}?take=50` returns `ConfigAuditEntry[]`.

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
    b.Options.Scope = "PaymentService";
    b.Options.AuditReads = true;
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
migrations or DBA tools are not first-class.

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

The screenshots cover the major features: entries list, editing, history with diff, bulk
operations, import/export, scope selector, and the access warning banner.

## Feature scope

- SQL Server and PostgreSQL via EF Core
- Hierarchical keys; Scope + Environment + TenantId + Key scoping
- Polling-based reload with immediate-reload signal on HTTP mutations
- Embedded React editor UI with CRUD, secret masking, scope badges, view-mode toggle,
  per-row audit history, and a global Audit Log timeline
- Per-entry encryption via `IConfigEncryptor` (`IsSecret = true` → encrypted at rest via
  ASP.NET Core Data Protection or a custom implementation)
- Audit log (`DbConfig_AuditEntries`) with in-transaction writes for mutations and
  fire-and-forget read audits (opt-in)
- Multi-tenant configuration via `ITenantResolver`; `IOptionsSnapshot<T>` is automatically
  tenant-aware
- Cross-tenant typed reads via `ITenantConfigReader.GetForTenant<T>(tenantId)` (uses the
  same `services.Configure<T>(...)` registration) and raw-metadata reads via
  `IConfigStore` convenience overloads
- Authorization: open by default; opt into built-in cookie login via
  `IDbConfigCredentialValidator`, plug in `IDbConfigAuthorizationFilter`, or compose with
  the host's existing pipeline via `RequireAuthorization`
- Auto-migrating schema (`SchemaMode.CreateIfMissing` default); production teams that
  prefer DBA-owned schema use `SchemaMode.None` plus `DbConfigMigrator` helpers
- `IncludeScopes` — pull config from one or more shared scopes with explicit precedence
- `MapDbConfigHttp(scopeFilter: "X")` — per-scope authorization at the group level

**Known limitations:**

- No audit log retention pruner (manual cleanup documented in
  [Audit retention](https://moberg.github.io/db-config/operations/audit-retention))
- Direct DB mutations bypass the reload signal AND audit log — always mutate via the API
- Two `EfCoreConfigStore` instances per host (polling provider + HTTP layer) both pointed
  at the same DB. They share no in-process state by design — the DB is the source of
  truth and the reload signal coordinates cache invalidation
- Ephemeral Data Protection key ring by default — see Security section for
  `PersistKeysToXxx` configuration

See [Releases](https://moberg.github.io/db-config/releases) for the changelog.
