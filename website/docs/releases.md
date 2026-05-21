---
sidebar_position: 99
---

# Releases

## v0.13.0 (2026-05-21)

Minor release. Three independent shifts bundled because they all touched the schema
layer and the library hasn't shipped to production yet — clean break is cheaper than
three sequential migrations.

### 1. Configurable schema (was hard-coded `dbo` / `public`)

- **`DbConfigOptions.Schema`** — pass `"app_config"` (or any string) to land the tables
  in a custom schema. `null` falls back to the database default (`dbo` on SQL Server,
  `public` on PostgreSQL). New default for fresh installs: `"configuration"`.
- The schema flows end-to-end: the SQL script's `CREATE SCHEMA` / `CREATE TABLE`
  statements use it, the EF Core runtime model points the DbContext at the same place,
  and the polling provider's queries hit the same tables. Implemented via
  `DbConfigOptionsExtension` (an `IDbContextOptionsExtension` carrying the schema
  through the EF Core options pipeline) plus `DbConfigOptions.Schema` flowing into the
  per-provider raw-SQL migrator.

### 2. Migration system replaced with idempotent raw SQL

EF Core migrations are gone. Each provider package ships a single embedded SQL script
that is fully idempotent (`IF NOT EXISTS` on every `CREATE`).

- Removed: `DbConfigMigrationsAssembly`, all `Migrations/` folders, the `__EFMigrationsHistory`
  table, the EF Core migration tooling dependency.
- Added: `SqlServerDbConfigMigrator` and `PostgreSqlDbConfigMigrator` static helpers.
  Public API:
  - `MigrateAsync(connectionString, schema = "configuration", ct)` — apply the script
  - `GetCreateScript(schema = "configuration")` — return the substituted SQL for offline use
- `DbConfigBuilder.SetMigrator(...)` is set internally by each provider's `Use*`
  extension. `SchemaMode.CreateIfMissing` (default) calls this migrator synchronously
  before the polling provider's first `Load()`.
- Generic `DbConfigMigrator` and `SqlServerDbConfigOptions.ForSqlServer(...)` /
  `PostgreSqlDbConfigOptions.ForPostgreSql(...)` helpers no longer return EF-wired
  `DbContextOptions` for migration use. They still exist for ad-hoc DbContext
  construction (e.g. integration tests) and now take the schema argument directly.

### 3. PG snake_case via convention; entity / record renames

- **PostgreSQL** uses `EFCore.NamingConventions` (snake_case) on the runtime model.
  Tables: `config_entries`, `audit_entries`. Columns: `scope`, `tenant_id`,
  `modified_utc`, etc. Indexes/PKs: `ix_config_entries_*`, `pk_config_entries`,
  matching EF defaults under the rewriter.
- **SQL Server** stays PascalCase by default: tables `ConfigEntries`, `AuditEntries`,
  PKs `PK_ConfigEntries` / `PK_AuditEntries`, indexes `IX_ConfigEntries_*`,
  `IX_AuditEntries_*`.
- Internal EF entity classes renamed: `ConfigEntryEntity` → `ConfigEntry`,
  `ConfigAuditEntryEntity` → `AuditEntry`. With matching `DbSet<>` property names
  (`ConfigEntries`, `AuditEntries`), EF default conventions now drive the table names
  directly — there is no explicit `ToTable(...)` in `OnModelCreating`. This is what
  lets the snake_case convention rewriter rewrite cleanly: explicit table-name strings
  would have defeated it.
- **Public records renamed (breaking)**: `DbConfig.Core.ConfigEntry` →
  `ConfigEntryRecord`; `DbConfig.Core.ConfigAuditEntry` → `ConfigAuditEntryRecord`.
  This frees the `ConfigEntry` / `AuditEntry` identifiers for the internal entity
  classes (different namespaces, but the C# 8.0 naming-collision rules made the previous
  arrangement painful to read in `EfCoreConfigStore`).

### Breaking changes

- **Table names changed.** SQL Server: `DbConfig_Entries` → `ConfigEntries`,
  `DbConfig_AuditEntries` → `AuditEntries`. PostgreSQL: identifiers were not stable
  before v0.13.0 so this is the first stable layout. Drop existing dev databases and
  let the new script create the new tables — or `DROP TABLE` the old ones and run
  the migrator manually.
- **Default schema is now `"configuration"`** (was the database default). Pin
  `DbConfigOptions.Schema = null` to keep the old layout if you cannot rename.
- **Public C# record renamed**: `ConfigEntry` → `ConfigEntryRecord`,
  `ConfigAuditEntry` → `ConfigAuditEntryRecord`. Update any consumer code that
  imports these types. JSON wire format (and therefore TypeScript types) is unchanged.
- **EF Core migration helpers removed**: `DbConfigMigrator` (in
  `DbConfig.EntityFrameworkCore`) and the `MigrationsAssembly` configuration are gone.
  Switch to the provider-specific `SqlServerDbConfigMigrator` / `PostgreSqlDbConfigMigrator`.

## v0.12.0 (2026-05-20)

Minor release. The primary scope column (logical app identifier) is now consistently
called `Scope` across the entire stack: database, C# entities, `IConfigStore` interface,
HTTP routes / query strings / request bodies, TypeScript types, the UI, and the sample
app. The UI label, badges, and `IncludeScopes` option already used this terminology;
everything else now matches.

Also unifies `ModifiedUtc` as `DateTimeOffset` across both tables — `DbConfig_Entries`
used to map to `datetime2` (DateTime), `DbConfig_AuditEntries` already mapped to
`datetimeoffset` (DateTimeOffset). Both now use `datetimeoffset` on SQL Server and
`timestamp with time zone` on PostgreSQL, eliminating the manual conversions in the
store layer.

- **`DbConfigOptions.Scope`** — primary scope for the host (was previously a separate
  property name)
- **`IConfigStore` parameters** — `string scope` and `IReadOnlyList<string> scopes`
  everywhere
- **HTTP API** — routes `/{scope}/{environment}/{*key}` (and audit variants); query string
  `?scope=` on `GET /` and `GET /audit`; request body field `scope` on `ConfigEntry` JSON
- **Database schema** — column `Scope` on both `DbConfig_Entries` and `DbConfig_AuditEntries`;
  indexes `UX_DbConfig_Entries_Scope_Environment_TenantId_Key` and
  `IX_DbConfig_Audit_Scope_Environment_TenantId_Key_ModifiedUtc`
- **TypeScript** — `ConfigEntry.scope`, axios client, demo data, Playwright fixtures
- **Sample `appsettings.json`** — `DbConfig:Scope`

Schema migrations were squashed into a fresh `InitialCreate` since the library isn't yet
running in production. Existing dev databases must be recreated (`docker compose down -v
&& docker compose up -d` for the sample).

**UI improvement:** scope / environment / tenant inputs in the scope selector and create
dialog now offer autocomplete suggestions based on the values currently in the entries
table (via a native HTML `<datalist>`).

**Breaking changes:** all C# / TypeScript / HTTP / DB consumers must update to the new
names. There is no back-compat shim; rename in your own code or pin to v0.11.x.

## v0.11.2 (2026-05-20)

Patch release. Adds `ITenantConfigReader` so application code can read another tenant's
typed settings without changing how sections are named — reuses the consumer's existing
`services.Configure<T>(GetSection("..."))` registration.

- **`ITenantConfigReader.GetForTenant<T>(tenantId)`** — binds `T` using the section path the
  consumer already registered. Internally sets an `AsyncLocal` tenant override on the
  polling provider for the duration of the call, then resolves `IOptionsSnapshot<T>` in a
  fresh DI scope. `PostConfigure<T>`, code-based configurators, and custom section paths
  all run exactly as they would for a normal request.
- **No DB hit per call** — the polling provider has all tenants in memory; the reader does
  in-memory dictionary lookups.
- **Concurrent-safe** — AsyncLocal isolates concurrent calls on different async flows; the
  override never leaks to the host's ambient `IConfiguration` after the call returns.
- **Complements `IConfigStore` typed overloads** — `IConfigStore.GetForTenantAsync<T>` (raw
  metadata: IsSecret, ModifiedUtc) remains the right tool for admin tooling and types
  without an `IConfigureOptions<T>` registration.

**Breaking changes:** none.

## v0.11.1 (2026-05-20)

Patch release. Adds ergonomic convenience overloads to `IConfigStore` so consumer code
reading another tenant's settings doesn't have to pass `scope` / `environment` on every
call.

- **Six default-interface overloads on `IConfigStore`:** `GetAsync(key, ct)`,
  `GetAllAsync(ct)`, `GetForTenantAsync(tenantId, key, ct)`, `GetAllForTenantAsync(tenantId,
  ct)`, `GetAsync<T>(ct)`, and `GetForTenantAsync<T>(tenantId, ct)`. `Scope` and
  `Environment` come from `DbConfigOptions`; current-tenant overloads consult the registered
  `ITenantResolver`.
- **Typed-bind section name is `typeof(T).Name` verbatim** — `StripeOptions` reads from
  `StripeOptions:` keys (not `Stripe:`). Intentionally diverges from
  `services.Configure<T>(GetSection("Stripe"))` to avoid silent breakage on refactors. Two
  viable patterns documented in [Programmatic access to configuration](./configuration/programmatic-access.md).
- **Targeted DB queries** for all overloads — no full-scope scans.
- **Generic-arity stripping** — `MyOptions<int>` → `MyOptions:` prefix.
- **Whitespace-only tenant ids** fall back to global (never queried literally).
- **Custom `IConfigStore` impls** that don't carry ambient state throw `NotSupportedException`
  from the convenience overloads; explicit-arg methods keep working.

**Breaking changes:** none. Default-interface methods do not break existing implementers.

## v0.11.0 (2026-05-20)

Minor release. Refactors the built-in cookie login to match sister project Warp's
architecture: the login UI is now part of the React SPA bundle instead of being
server-rendered HTML. Visual consistency with the dashboard (dark mode, shared theme
tokens, typography), screenshot pipeline captures the real form, and the flow is now
standard SPA auth.

- **`BuiltInLoginEndpoints` is JSON-only:** `POST /api/auth/login`, `POST /api/auth/logout`,
  `GET /api/auth/status`.
- **`EmbeddedStaticFileMiddleware` injects `window.dbConfig = { apiPrefix, hasBuiltInLogin }`**
  into the SPA shell so React knows whether to gate the UI on login.
- **`ui/src/pages/LoginPage.tsx`** renders the login form with shared theme tokens.
- **App-level auth gate** checks `/api/auth/status` on mount; routes to LoginPage if
  unauthenticated and the built-in login is enabled.
- **Sign-out link** in the header.

**Consumer code unchanged.** `opts.UseBuiltInLogin<TValidator>()` works exactly the same.
`IDbConfigCredentialValidator` contract unchanged. Cookies issued by the previous version
continue to validate after upgrade — cookie payload format unchanged.

**Breaking changes:** none for consumers using `opts.UseBuiltInLogin<TValidator>()`. The
HTTP wire format changed (form POST → JSON POST), but the only caller of those endpoints
is the SPA bundle shipped by the library.

## v0.10.2 (2026-05-19)

Patch release. Closes a real DX gap: consumers no longer have to construct a
`DbConfigDbContext` manually and call `MigrateAsync` before `AddDbConfig`. The library
auto-creates its schema by default, matching Hangfire / Marten / Wolverine conventions.

- **`SchemaMode` option** on `DbConfigOptions` — defaults to `SchemaMode.CreateIfMissing`.
  `AddDbConfig` applies any pending migrations synchronously before the polling
  provider's first `Load()`. Production teams that prefer DBA-controlled schema apply
  `SchemaMode.None`.
- **`DbConfigMigrator` static helper** in `Moberg.DbConfig.EntityFrameworkCore` —
  `MigrateAsync`, `GenerateCreateScript`, and `GenerateMigrationScript(idempotent: true)`
  for offline / CI/CD application of the schema.
- **Per-provider options helpers** — `SqlServerDbConfigOptions.ForSqlServer(connStr)`
  and `PostgreSqlDbConfigOptions.ForPostgreSql(connStr)` hide the `MigrationsAssembly`
  magic string.
- **UTC value converters** for `DateTime` and `DateTimeOffset` `ModifiedUtc` columns —
  defense-in-depth so SQL Server's `datetime2` columns can never silently store a
  non-UTC instant.
- **Sample (`samples/PaymentsApi`) drops its manual `ApplyMigrationsAsync` helper** —
  `AddDbConfig` handles schema setup automatically.

**Breaking changes:** none. Default behaviour change (`SchemaMode.CreateIfMissing` runs
migrations automatically) only takes effect on hosts upgrading from v0.10.x; the
migrations applied are exactly what `MigrateAsync` would have applied, and
`__EFMigrationsHistory` tracks state.

**Shelved (design captured):** a full time-bounded configuration overrides spec lives at
`docs/specs/2026-05-19-v0.11.0-time-bounded-overrides.md`. Marked `Status: Shelved`.

## v0.10.1 (2026-05-19)

Patch release with several UI fixes and one notable feature.

- **Global Audit Log page** — `GET /api/dbconfig/audit` returns the flat audit timeline
  with optional `?scope=&environment=&tenantId=&keyPrefix=&action=&take=` filters. The
  admin UI gets a new **Audit Log** tab next to **Entries** so Delete events (whose
  entries no longer exist in the grid) are visible. See
  [Audit Log page](./ui-editor/audit-log-page.md).
- **`IConfigAuditStore.QueryAsync`** — new store method backing the endpoint;
  implemented in `InMemoryConfigAuditStore` and `EfCoreConfigAuditStore`.
- **Favicon** — Vite-emitted SVG embedded under the UI prefix and served alongside the
  rest of the bundle.
- **StaticFileMiddleware refactor** — replaced hand-rolled per-file `MapGet` routes
  with ASP.NET's `StaticFileMiddleware` + `EmbeddedFileProvider`. ETag, conditional
  GETs, range requests, and cache headers all come for free.
- **Tree-view alignment** — leaf checkboxes and group chevrons now share the same X
  coordinate per depth. Group rows use `colSpan` so the chevron lives in the same
  column as a leaf's checkbox.
- **Dialog width fix** — the Dialog primitive's inner wrapper was unsized, capping
  every modal at ~290px regardless of the `size` prop. Wrapper now `w-full`; Edit and
  Create dialogs are at `xl` (1152px).
- **Clickable rows** — clicking anywhere on an entries row (except the checkbox,
  secret-reveal eye, and action buttons) opens Edit.
- **Enum serialization** — `ConfigAuditAction` is now serialized as its string name
  (`"Insert"`, `"Update"`, ...) in HTTP responses instead of the underlying integer.
- **Sample seed** — expanded with depth-0/1/2/3 secrets and varied audit history
  (`Stripe:DefaultCurrency` updated USD→EUR→GBP; `Legacy:OldSetting` inserted then
  deleted so the audit log shows a Delete with no current entry).

**Breaking changes:** none. The library's public surface is unchanged.

## v0.10.0 (2026-05-19)

Second minor release. Quality-of-life improvements over v0.9.0, plus a relaxation of the
previous "host owns all auth" rule.

- **Built-in cookie login** for the admin UI. Implement `IDbConfigCredentialValidator`
  (username/password → `ClaimsPrincipal`), wire via `opts.UseBuiltInLogin<TValidator>()`.
  ASP.NET Data Protection signs the cookie. Sliding 7-day expiry by default.
- **`MapDbConfigAdmin(prefix, configure)`** — single-call mount for UI + HTTP API under
  one prefix. Both surfaces share the same cookie filter. Mirrors sister project Warp's
  `UseWarpUI` UX.
- **`IDbConfigAuthorizationFilter` + `LocalRequestsOnlyAuthorizationFilter`** —
  per-request filter for non-form auth (header check, IP allowlist, JWT, etc.). Custom
  filters compose with the built-in cookie via the new options.
- **`UnauthorizedRedirectUrl`** — alternative path for hosts with their own existing
  login page.
- **Flat `GET /` entries endpoint** with optional query-string filters
  (`?scope=&environment=&tenantId=&keyPrefix=&take=`). Replaces the old path-based
  list endpoint. UI now loads all entries on mount — no Scope/Environment input
  required.
- **UI multi-scope display** — entries table shows Scope + Environment + Tenant
  columns. Edit / delete / history actions use the entry's own coordinates, so
  cross-scope operations work correctly.
- **Softer dark mode palette** — off-black background and slightly compressed contrast
  for less eye strain.
- **Deeper sample seed** in `samples/PaymentsApi` — 3- and 4-level nested keys
  (`Notifications:Email:Smtp:Host`, `Features:Experiments:Checkout:V2:Enabled`) plus a
  second `Notifications` app to showcase the flat endpoint.

**Breaking changes:**
- `GET /{scope}/{environment}` (path-based list) removed. Use `GET /` with optional
  query-string filters.
- `IDbConfigAuthorizationFilter` and `IDbConfigCredentialValidator` live in
  `Moberg.DbConfig.Http` (not `Moberg.DbConfig.Ui`).

The previous §0.3 ("NEVER bake authorization into the package") is replaced by an
opt-in model: defaults remain open access, auth is configured via `DbConfigUiOptions`.
Hosts that already chain `.RequireAuthorization("policy")` keep working with no changes.

## v0.9.0 (2026-05-18)

Initial public release. Adds tenant as a fourth scoping dimension on top of the v0.8.0
internal stack.

- **`ITenantResolver`** — consumer-implemented interface with one method (`string?
  Resolve()`). Reads tenant identity from whatever source fits the host's auth model
  (JWT claim, header, route, subdomain). Registered via `b.AddTenantResolver<T>()`
  inside `AddDbConfig`.
- **`TenantId` as the fourth column** on `ConfigEntry` and `ConfigAuditEntry`. Empty
  string (`""`) is the global default sentinel; non-empty strings are tenant-specific
  overrides. Case-sensitive (inherits `Latin1_General_100_BIN2` / `"C"` collation).
- **`DbConfigConfigurationProvider.TryGet` is tenant-aware.** Every
  `IConfiguration[key]` read calls the resolver, then selects the tenant-specific entry
  with global-default fallback. `IOptionsSnapshot<T>` rebinds per request scope, so
  consumers get the right tenant's values transparently with no custom options API.
- **`IOptions<T>` gotcha** documented loudly: singleton-cached, factory runs once at
  startup with no request scope → always global values. Tenant-aware types MUST use
  `IOptionsSnapshot<T>`. See [Multi-tenant config](./configuration/multi-tenant.md).
- **Schema migration** `AddTenantIdColumn` widens the unique constraint to
  `(Scope, Environment, TenantId, Key)` and applies case-sensitive collation to the
  new column.
- **UI** — Tenant column in the Entries grid (Default badge for global entries; colored
  chip for tenant-specific); Tenant input in the ScopeSelector; CreateEntryDialog
  pre-fills `TenantId` from the current scope.

## v0.6.0 (2026-05-17)

Quality-of-life improvements: opt-in read auditing, UI feature deepening, and flexible
encryptor registration.

- **Read auditing (opt-in):** `DbConfigOptions.AuditReads = true` writes fire-and-forget
  audit rows with `Action=Read` on every `GET` request. The history endpoint is excluded
  from read auditing to prevent recursion.
- **UI diff view:** per-row "Compare to previous" in the audit history dialog shows a
  character-level diff with JSON pretty-printing. Values over 100 KB show a "too large
  to diff" message.
- **UI bulk operations:** row checkboxes + `BulkActionsToolbar` for Toggle IsSecret, Move
  to scope, and Delete selected. Per-item progress and failure reporting.
- **UI import / export:** export the current scope as `appsettings.json` with a `_dbconfig`
  metadata sidecar; import from JSON with Overwrite / Skip / Error collision policies.
- **Type-mapped `IConfigEncryptor`:** `services.AddSingleton<IConfigEncryptor, MyImpl>()`
  now works (previously required an instance registration). Decryption is deferred until
  after `host.StartAsync()` via a `DbConfigEncryptorActivator` hosted service. Reading a
  secret before host start throws a clear `InvalidOperationException`.

## v0.5.0 (2026-05-17)

Production hardening: at-rest encryption, full audit log, and collation fix.

- **Per-entry encryption:** entries with `IsSecret = true` are encrypted at rest using
  ASP.NET Core Data Protection. Default key ring is ephemeral — configure
  `PersistKeysToFileSystem` / `PersistKeysToAzureBlobStorage` etc. before `AddDbConfig`.
- **Audit log:** `DbConfig_AuditEntries` table records every Upsert and Delete atomically
  (in-transaction with the mutation). `Action` is one of `Insert`, `Update`, `Delete`.
  `OldValue` / `NewValue` are stored as ciphertext for secret entries.
- **HTTP audit endpoint:** `GET /{app}/{env}/audit/{*key}?take=N` returns decrypted audit
  history ordered most-recent-first, capped at 500 rows.
- **UI history dialog:** per-row "History" button opens the audit history dialog with
  secret masking and reveal toggle.
- **Case-sensitive scope columns:** binary collation on `Scope`, `Environment`, and `Key`
  closes the mismatch between HTTP `scopeFilter` (ordinal comparison) and the DB query.
  `CaseSensitiveScopeColumns` migration requires a maintenance window for live tables.
- **`DbConfigOptions.EnableAuditLog`:** set to `false` to opt out of audit writes entirely.

## v0.4.0 (2026-05-17)

Multi-scope configuration and per-scope authorization.

- **`DbConfigOptions.IncludeScopes`:** list of additional Scope scopes to include in
  polling reads. Precedence: listed scopes lowest-first, own `Scope` always wins.
- **Multi-scope polling:** one `SELECT ... WHERE Scope IN (...)` query covers all scopes.
  A change in any included scope triggers reload in every consumer within one poll interval.
- **`MapDbConfigHttp(scopeFilter: "X")`:** optional parameter restricts all routes in the
  group to a single Scope. Writes to other scopes return `403`. Use multiple groups with
  different filters for app-team vs platform-team auth separation.
- **UI scope badge:** each row shows the source `Scope` as a colored badge.
- **UI view mode toggle:** Mine / Shared / All view modes.
- **Scope selector persists to `localStorage`.**

## v0.3.0 (2026-05-17)

Single-call registration on `IHostApplicationBuilder`.

- **`builder.AddDbConfig(b => ...)`** replaces the v0.2.0 two-call shape. Works for both
  `WebApplicationBuilder` (ASP.NET Core) and `HostApplicationBuilder` (worker services).
- Polling-side store is constructed directly from the lambda's captured connection string
  (no `BuildServiceProvider()` call).
- `IDbConfigReloadSignal` is resolved lazily from a registration marker — no bridge code
  required in consumer `Program.cs`.

## v0.2.0 (2026-05-17)

Architecture refinement: package extraction, targeted single-key reads, and unique-
constraint detection.

- **`Moberg.DbConfig.EntityFrameworkCore` extracted** from `Moberg.DbConfig.Core`.
  Consumers writing custom non-EF stores no longer pull EF Core transitively.
- **`IConfigStore.GetAsync`:** targeted single-row query for HTTP GET single. No longer
  scans the full scope.
- **`IUniqueConstraintDetector` strategy:** provider-specific exception handling lives in
  each provider package. `Core` no longer contains SQL Server or PostgreSQL exception names.
- **Two-call DI shape** (`builder.Services.AddDbConfig(...)` + `builder.Configuration.AddDbConfig()`).
  Superseded by v0.3.0's single-call shape.

## v0.1.0 (2026-05-16)

Initial release.

- **`Moberg.DbConfig.Core`:** `IConfigurationSource`, `IConfigurationProvider`, polling
  loop with `IChangeToken`, `IConfigStore` abstraction, `InMemoryConfigStore` for tests.
- **`Moberg.DbConfig.Provider.SqlServer`:** EF Core SQL Server store + migrations.
- **`Moberg.DbConfig.Provider.PostgreSql`:** EF Core PostgreSQL (Npgsql) store + migrations.
- **`Moberg.DbConfig.Http`:** JSON API endpoints — list, get-single, upsert, delete, reload.
  Host-owned authorization via `RouteGroupBuilder`.
- **`Moberg.DbConfig.Ui`:** embedded React SPA (Vite + React + TypeScript + Tailwind +
  shadcn) with CRUD, secret masking, scope badges, and `MapDbConfigUi` mount point.
- **`POST /reload`:** in-process reload signal endpoint.
- **Hierarchical keys:** `:` separator round-trips through `IConfiguration`.
- **Polling with reload signal:** configurable interval (default 30s) plus immediate-reload
  on HTTP mutation.
