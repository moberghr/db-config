---
sidebar_position: 99
---

# Releases

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
- **Case-sensitive scope columns:** binary collation on `AppName`, `Environment`, and `Key`
  closes the mismatch between HTTP `scopeFilter` (ordinal comparison) and the DB query.
  `CaseSensitiveScopeColumns` migration requires a maintenance window for live tables.
- **`DbConfigOptions.EnableAuditLog`:** set to `false` to opt out of audit writes entirely.

## v0.4.0 (2026-05-17)

Multi-scope configuration and per-scope authorization.

- **`DbConfigOptions.IncludeScopes`:** list of additional AppName scopes to include in
  polling reads. Precedence: listed scopes lowest-first, own `AppName` always wins.
- **Multi-scope polling:** one `SELECT ... WHERE AppName IN (...)` query covers all scopes.
  A change in any included scope triggers reload in every consumer within one poll interval.
- **`MapDbConfigHttp(scopeFilter: "X")`:** optional parameter restricts all routes in the
  group to a single AppName. Writes to other scopes return `403`. Use multiple groups with
  different filters for app-team vs platform-team auth separation.
- **UI scope badge:** each row shows the source `AppName` as a colored badge.
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
