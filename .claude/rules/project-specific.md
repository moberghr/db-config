---
paths:
  - "src/**"
  - "samples/**"
  - "ui/**"
axes:
  decision: structure
  topic: project
  scope: project
---

# Project-Specific Patterns

## §8.1 — `Scope` + `Environment` Scoping

Every `ConfigEntry` is uniquely identified by the composite key `(Scope, Environment, Key)`.
With `TenantId` this forms the unique constraint (`Scope, Environment, TenantId, Key`) on the
entries table. There is no row-level tenant
isolation beyond this triple — all entries for the same `(Scope, Environment)` are returned
together by `GetAllAsync`.

- `Scope` — logical application name (e.g. `"MyApp"`). Set once in `DbConfigOptions`.
- `Environment` — deployment environment (e.g. `"Production"`, `"Staging"`). Typically bound
  to `builder.Environment.EnvironmentName`.
- `Key` — hierarchical key using `:` as the separator (matches `IConfiguration` convention).
  `Section:Sub` stores as `"Section:Sub"` and round-trips as `IConfiguration["Section:Sub"]`
  or `IConfiguration.GetSection("Section")["Sub"]`.

Both columns are indexed alongside `ModifiedUtc DESC` for the polling watermark query. Never
add a secondary index on `Key` alone — the composite unique constraint is already indexed.

## §8.2 — `IsSecret` Flag

`IsSecret` drives **both UI masking and at-rest encryption**. When `true`:

- The React editor masks the value on screen (shows `•••••`)
- `EfCoreConfigStore.UpsertAsync` calls `IConfigEncryptor.Protect()` before writing to DB
- `EfCoreConfigStore.GetAsync` / `GetAllAsync` call `IConfigEncryptor.Unprotect()` on read

When `false`, values flow verbatim — no encrypt/decrypt overhead. This is intentional for
debuggability (feature flags, log levels, polling intervals, etc. stay readable in DB tools).

**HTTP GET responses always return plaintext** (the store decrypts before returning to the
HTTP layer). Callers do not see ciphertext.

**Audit rows** store OldValue/NewValue in the same form as the main column (ciphertext when
IsSecret=true). The audit read endpoint decrypts before returning to callers.

**Post-hoc flag flip:** flipping `IsSecret` after a row exists produces undefined behavior.
See §2.12 for the edge case details. Document this to consumers; the package intentionally
does not silently re-encrypt. This is documented in `architecture.md` §2.12 and in the README Security
section.

## §8.3 — Hand-Written SQL Schema (no EF Core migrations)

There are **no EF Core migrations in this repo** — no `Migrations/` folder, no `Designer.cs`, no
`ModelSnapshot.cs`, no `MigrationsAssembly` configuration. `DbConfigDbContext.OnModelCreating`
deliberately carries no table-name literals; names derive from the entity sets plus the configured
schema.

Schema is created by hand-written SQL, one script per provider, executed by that provider's migrator:

- `src/core/providers/DbConfig.Provider.SqlServer/Sql/InitialCreate.sql` — run by
  `SqlServerDbConfigMigrator` (tables `[ConfigEntries]`, `[AuditEntries]`)
- `src/core/providers/DbConfig.Provider.PostgreSql/Sql/InitialCreate.sql` — run by
  `PostgreSqlDbConfigMigrator` (tables `"config_entries"`, `"audit_entries"`, snake_case via
  `EFCore.NamingConventions`)

To change the schema:

- Edit the entity (`ConfigEntry` / `AuditEntry`) in `DbConfig.EntityFrameworkCore`.
- Edit **both** `InitialCreate.sql` scripts — missing one silently breaks that engine only.
- Run the tests on both engines to verify the schema applies cleanly (§8.4).

If the entity set ever grows enough to warrant real EF migrations, that is a decision to record in
`.claude/references/decisions.md`, not an incremental change.

## §8.4 — Dual-Database Testing

Every test that touches `IConfigStore`, `EfCoreConfigStore`, or any HTTP endpoint that calls
the store MUST have coverage on both SQL Server and PostgreSQL. Use the Testcontainers
fixtures:

```csharp
[Collection("SqlServer")]
public class MyStoreSqlServerTests(SqlServerFixture db) { ... }

[Collection("PostgreSql")]
public class MyStorePgTests(PostgreSqlFixture db) { ... }
```

Test categories:

| Category | What it covers |
|---|---|
| `Unit` | Pure logic, `InMemoryConfigStore`, no containers required |
| `SqlServer` | `EfCoreConfigStore` + endpoints against a real MSSQL Testcontainer |
| `PostgreSql` | `EfCoreConfigStore` + endpoints against a real PG Testcontainer |
| `E2E` | Full host pipeline via `Microsoft.AspNetCore.TestHost` |

Never add a SQL Server-only test for behavior that is equally applicable to PostgreSQL. The
`IUniqueConstraintDetector` implementations are engine-specific — unit-test each detector in
isolation, then use the shared store tests to verify the integration.

## §8.5 — xUnit v3 MTP Runner Quirk

The test project uses `UseMicrosoftTestingPlatformRunner=true` (xUnit v3 MTP runner). Running
via `dotnet test` may exit with code 5 ("zero tests ran") even when tests exist. This is a
known xUnit v3 / MTP interop issue.

Always run tests by invoking the test executable directly:

```bash
cd src/tests/DbConfig.Tests/bin/Debug/net8.0
./DbConfig.Tests            # ./DbConfig.Tests.exe on Windows
```

Or with a filter:

```bash
./DbConfig.Tests --filter-trait "Category=Unit"
./DbConfig.Tests --filter-trait "Category=SqlServer"
./DbConfig.Tests --filter-trait "Category=PostgreSql"
```

Current suite: 447 tests (verified 2026-09-21 via `--list-tests`). Packages and tests multi-target,
so pick the `bin/Debug/net8.0` or `bin/Debug/net10.0` executable deliberately. Any new behavior requires at least one
test. Any new store-touching behavior requires tests on both engines.

## §8.6 — Watermark-Based Polling + DELETE Caveat

The polling loop uses `GetLatestModifiedUtcAsync` as its change detector. It only sees a
change if some row's `ModifiedUtc` has advanced since the last poll.

**Caveat — direct-SQL DELETE:** If a row is deleted directly in the database (not via the
HTTP `DELETE` endpoint), the watermark does not advance. The polling provider will not
reflect the deletion until another row's `ModifiedUtc` advances for an unrelated reason.

The HTTP `DELETE /{scope}/{env}/{*key}` endpoint:
1. Calls `IConfigStore.DeleteAsync`.
2. Calls `IDbConfigReloadSignal.TriggerReload()`.

Step 2 forces an immediate reload regardless of the watermark. Direct-SQL mutations (DBA
tools, migrations) skip this signal and are therefore invisible until the watermark moves.

Document this in any tooling that directly mutates the entries table. Never attempt to
"fix" it inside the provider — the invariant is by design for v0.1.0.

## §8.7 — Demo Auth (NOT for Production)

`samples/PaymentsApi/` demonstrates the **unified `MapDbConfigAdmin`** mount
with built-in cookie login (`opts.UseBuiltInLogin<AppSettingsCredentialValidator>()`).
The validator checks the submitted password against `Auth:Password` in
`appsettings.json`. One cookie covers both the UI (`/admin/dbconfig`) and the
HTTP API (`/admin/dbconfig/api`) — the React app calls its own backend right
after sign-in with no separate auth scheme.

This pattern is NOT production-grade — a single shared password is fine for a
demo but real deployments need per-user credentials. Production hosts should
either:
- Integrate the built-in cookie login with a real `IDbConfigCredentialValidator`
  that hashes/verifies passwords against a database or identity provider, or
- Skip the built-in surface entirely and use pattern 3 from the auth docs —
  `.RequireAuthorization("DbConfigAdmin")` with an existing OIDC / Windows Auth /
  JWT scheme on the host (works on both the unified `MapDbConfigAdmin` return
  values and the v0.9.0 split form).

See `security.md` §1.6 for the auth boundary and `architecture.md` §2.8 for the
composition patterns. The package never owns identity; the consumer-implemented
`IDbConfigCredentialValidator` is the security boundary for the built-in cookie
flow.

## §8.8 — Shared Scopes: Conventions and Authorization

**Naming:** shared scopes are conventional, not reserved. Common names:
- `Shared` — config visible to all apps in an organization
- `PlatformDefaults` — defaults the platform team owns
- `OrgGlobals` — values that span tenants
Pick names that are obviously not real app names. Avoid `Default`, `Common`, `Base` — too easy to collide with a real app.

**Precedence convention:** list shared scopes lowest-precedence-first. The configured
`Scope` is always highest. Standard pattern:
```csharp
IncludeScopes = ["OrgGlobals", "PlatformDefaults", "Shared"];
```

**Auth pattern (NOT FOR PROD without adaptation):**
- App teams: `scopeFilter: <Scope>` + AppTeamAdmin policy
- Platform team: `scopeFilter: "Shared"` (or a dedicated platform scope) + PlatformAdmin policy
- Separate route prefixes for each group (e.g. `/api/dbconfig` and `/api/dbconfig-shared`)

**Never:** put real production secrets in a shared scope unless ALL apps that include it are
trusted to read them. `IsSecret` is a UI mask only — every app's process can read every byte.

**Testing:** when adding scoped behavior, include precedence tests with at least 3 scopes to
prove the ordering is stable (not just two-scope alternation).

## §8.9 — Encryption: what to mark IsSecret

**Mark IsSecret=true:** connection strings, API keys, OAuth client secrets, JWT signing
keys, passwords, encryption keys, third-party tokens.

**Leave IsSecret=false (plaintext):** feature flags, log levels, polling intervals,
URLs, tenant/scope names, public OAuth client IDs, numeric tuning parameters.

**Why the split:** non-secret values stay readable in DB tools (psql, SSMS, EF Core
LINQpad scripts). Operations gets debug-friendly visibility for ~80% of config; the
20% that's actually sensitive gets protected.

**Key persistence:** the default ASP.NET Data Protection key ring is ephemeral
(process-scoped, regenerated on every host startup). For multi-instance or restart-stable
deployments, configure persistence in `Program.cs`:

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .ProtectKeysWithCertificate(certThumbprint);
builder.AddDbConfig(b => { ... });   // picks up the configured ring via TryAddSingleton
```

**Key rotation:** Data Protection handles this automatically — new keys roll every 90
days; old keys retained for decryption indefinitely until you explicitly revoke them.

## §8.10 — Audit log: retention and queries

**Retention:** the package ships NO automatic pruner. Recommended pattern:

```sql
-- SQL Server (weekly job)
DELETE FROM AuditEntries
WHERE ModifiedUtc < DATEADD(day, -90, SYSUTCDATETIME());
```

```sql
-- PostgreSQL (weekly job)
DELETE FROM "audit_entries"
WHERE "modified_utc" < (NOW() - INTERVAL '90 days');
```

90 days is a reasonable default for non-regulated workloads. Compliance scenarios (PCI,
HIPAA, SOX) may require longer; check your auditor.

**Cross-scope reads:** the audit HTTP endpoint honors `scopeFilter`. A consumer with
`scopeFilter: "MyApp"` can read audit history only for keys under `MyApp/*`. Cross-scope
reads from that host return 403.

**Direct DB mutations:** if someone runs a SQL `UPDATE` or `INSERT` directly on
the entries table (bypassing the store), NO audit row is written. The audit log is
only as good as your discipline about always going through the API.

**Audit values encrypted:** IsSecret old/new values are stored as ciphertext. Querying
the audit table directly with SSMS won't reveal secret values — only the package's
audit reader (which has the encryptor) can decrypt them.

### Migration runbook for collation change

The `20260517000001_CaseSensitiveScopeColumns` migration acquires `Sch-M` (SQL Server) or
`AccessExclusiveLock` (PostgreSQL) on the entries and audit tables (`ConfigEntries`/`AuditEntries` on SQL Server, `config_entries`/`audit_entries` on PostgreSQL) for the
duration of the ALTER. For tables under live read load, quiesce the host (or use a maintenance
window) before applying. SQL Server does NOT support `ONLINE = ON` for collation changes. For
PostgreSQL, the lock is brief (text→text type change) but still blocks readers.

## §8.11 — Read auditing retention

Read audits, when enabled, can dominate the audit table by row count — every GET
produces a row. Recommended retention is shorter than for mutations:

```sql
-- SQL Server: keep read audits for 30 days, mutation audits for 90
DELETE FROM AuditEntries
WHERE Action = 'Read' AND ModifiedUtc < DATEADD(day, -30, SYSUTCDATETIME());

DELETE FROM AuditEntries
WHERE Action IN ('Insert', 'Update', 'Delete')
  AND ModifiedUtc < DATEADD(day, -90, SYSUTCDATETIME());
```

PostgreSQL equivalent uses `INTERVAL '30 days'`. Tune per your compliance posture.
The package ships no automatic pruner; document your retention SQL alongside your
DB maintenance scripts.

**Tracking key sentinels:** read audits for list endpoints use `Key='*'`. Filter
these separately if you want to track "list accesses" vs "single-key accesses":

```sql
-- Single-key reads (compliance-relevant for secret accesses)
SELECT * FROM AuditEntries
WHERE Action = 'Read' AND Key != '*' AND ModifiedUtc > @since;
```

## §8.12 — Pre-build secret reads with type-mapped encryptor

When using a type-mapped `IConfigEncryptor` registration (the common case for KMS
or Vault integrations with DI-injected dependencies), the polling provider can only
decrypt AFTER `host.StartAsync` has run the `DbConfigEncryptorActivator` hosted
service.

If a piece of code reads a secret config value during `ConfigureServices` or
inside another extension's bootstrap (before `host.Build` returns), it gets a clear
`InvalidOperationException`. The message instructs the developer to move the read
to a request handler, hosted service, or `OnStarted` callback.

**Why this is fine in practice:** ASP.NET Core configuration values are typically
read in three places:
1. `IOptions<T>` registrations (deferred — read lazily by consumers post-build) ✓ works
2. Request handlers (post-build) ✓ works
3. Background services (`IHostedService.StartAsync`, post-build) ✓ works

The pattern that DOESN'T work is reading `builder.Configuration["MySecret"]` directly
during `ConfigureServices` to pass to another extension method as a value. If you need
that pattern, use instance-registered encryption (no deferred decryption).

**Migration path for v0.5.0 → v0.6.0 consumers:** instance-registered encryptors continue
working with no code changes. Type-mapped is now a valid alternative for consumers
who prefer the standard DI registration syntax.

## §8.13 — Demo mode and screenshot tests

The UI ships with a demo mode that replaces the Axios HTTP client with an in-memory
adapter for deterministic screenshots and offline browsing.

**Activation:**
- Vite startup: `npm run dev -- --mode demo` sets `import.meta.env.MODE === 'demo'`
- Runtime query string: `?demo` appended to any URL
- Either trigger activates `ui/src/api/client.ts`'s demo-mode branch, lazily importing
  `ui/src/demo/adapter.ts` and replacing the API surface

**Demo data location:** `ui/src/demo/data.ts` — ~17 entries across 3 scopes
(`PaymentService`, `Shared`, `PlatformDefaults`) with realistic audit history.

**Bundle size guard:** the demo adapter is in a lazy chunk (~9 KB gzipped). Production
builds without `?demo` triggers do NOT load it. NEVER import demo files from production
code paths — always go through the runtime-gated dynamic import in `ui/src/api/client.ts`.

**Screenshot tests (`ui/e2e/screenshots.spec.ts`):** Playwright starts Vite in
`--mode demo` on port 5179. Each test navigates to `/?demo`, drives the UI to a known
state, and captures to `website/static/img/screenshots/{NN-name}.png`. Tests use
- `await page.locator('h2').filter({hasText: dialogTitle})` for dialog detection (the
  custom Dialog component lacks `role='dialog'` — improving this is tracked for v0.6.0+)
- `await page.emulateMedia({reducedMotion: 'reduce'})` for animation determinism

**Run:** `cd ui && npm run screenshots` (after `npm run screenshots:install` once).

## §8.14 — Multi-Tenant Conventions

v0.9.0 adds `TenantId` as a fourth scoping dimension on `ConfigEntry`. The following
conventions apply across all layers (store, provider, HTTP, UI):

**Empty string is the global default sentinel.**
`TenantId = ""` (empty string, not NULL) represents "applies to all tenants". This
is stored literally in the database column. `ConfigurationProvider.Data` (the base
`IConfiguration` dictionary) contains ONLY entries with `TenantId = ""` — these are
the fast-path global entries. Tenant-specific entries live in `_tenantData` and are
accessed via `TryGet` when `ITenantResolver.Resolve()` returns a non-empty id.

**Tenants are case-sensitive.**
Per the v0.5.0 collation fix, all four scope columns (`Scope`, `Environment`,
`TenantId`, `Key`) use case-sensitive collation. `"Acme"` and `"acme"` are distinct
tenant identifiers. Use consistent casing across all writes and reads. The resolver
implementation is responsible for normalizing casing — the package does not normalize.

**Host owns tenant resolution via `ITenantResolver`.**
The package ships `ITenantResolver` (interface) and `NullTenantResolver` (internal; returns
null). The host implements `ITenantResolver` and registers it via `b.AddTenantResolver<T>()`
inside the `AddDbConfig` block. The resolver reads from whatever source fits the host's
auth model (JWT claim, header, route, subdomain). This mirrors the §0.3 principle: the
package never owns identity or policy. The resolver is called on every `IConfiguration[key]`
read; it MUST be cheap — no I/O, no database calls.

**Fallback is in `TryGet`, not the store.**
`IConfigStore` does NOT fall back across tenant ids — it returns exactly the DB row for
the given `tenantId`, or null. Fallback (tenant-specific → global default → null) is
implemented in `DbConfigConfigurationProvider.TryGet`. Never duplicate fallback logic at
the store layer.

**`IOptions<T>` vs `IOptionsSnapshot<T>`.**
`IOptions<T>` is singleton-cached; the factory runs at startup with no request context;
the resolver returns null; the cached T has global values forever. Consumers MUST use
`IOptionsSnapshot<T>` (scoped per-request) for any tenant-aware type. Document this
constraint loudly. See AGENTS.md's tenancy pointer and `architecture.md` §2.15.

**Recommended memory ceiling: ~10K tenants × 100 keys (~200 MB).**
The polling provider loads ALL tenants into memory on each reload. Beyond this ceiling,
consider lazy per-tenant loading (tracked for v0.10.0+). Document this limit explicitly
in any host that expects large tenant counts.
