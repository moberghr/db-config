# Spec: Write-only secrets (`IsWriteOnly`)

- Date: 2026-10-21
- Branch: `feat/mark-secrets`
- Scope: `new-feature` (additive public API; binary-breaking for positional record constructors, pre-1.0)
- Spec mode: extended (rigor MAX). EARS lint clean, INVEST+C 16/16 (see Readiness notes).
- security_impact: `secrets-change`

## Summary

Add a per-entry `IsWriteOnly` flag, modelled on GitHub Actions secrets. Users can create or replace
a write-only value through the HTTP API and the UI, but they can never read it back. The host
application still reads the plaintext in-process through `IConfiguration` / `IConfigStore`.

`IsWriteOnly` is separate from `IsSecret`. Existing `IsSecret` entries stay revealable, so the
change is non-breaking for data. Write-only implies encryption at rest: the store sets
`IsSecret = true` whenever `IsWriteOnly = true`.

Today the masking is cosmetic. `SecretValueCell.tsx` hides the value in the browser, but the HTTP
layer returns plaintext (`website/docs/http-api/endpoints.md:51`, `:217`). The new flag is
therefore enforced in `Moberg.DbConfig.Http`, at every endpoint that serializes a value.

## Decisions (ambiguity gate, answered 2026-10-21)

| Question | Decision |
|---|---|
| Global switch or per entry? | Per entry only. |
| Should existing `IsSecret` entries become write-only? | No. `IsSecret` and `IsWriteOnly` are separate flags. |
| What do the UI bulk actions (Move, Toggle IsSecret) do with write-only entries? | They skip them and report them as failed with a reason. There is no server-side move endpoint (lesson L28). |
| What does Export do with write-only entries? | It leaves them out of the file and shows a notice listing them. |

Design consequences that follow without a separate decision:

- **No flip-read-flip downgrade.** `PUT` is a full replace (`UpsertEntryEndpoint.cs:25-35`), so
  clearing `IsWriteOnly` always comes with a value supplied in the same request. The old value is
  never returned. No extra server guard is needed. A test pins this behavior.
- **No `hasValue` field.** The response stays `ConfigEntryRecord` with `value: null` and
  `isWriteOnly: true`. The UI shows "write-only, set <modifiedUtc> by <modifiedBy>". Adding an
  Http DTO only to carry one boolean is not worth a new contract.
- **Server-side copy/promote does not exist today.** The earlier chat suggestion that it "keeps
  working" was wrong. All bulk operations run as client loops (`BulkActionsToolbar.tsx:62-70`).
  The skip decision covers this.

## Success criteria

| Id | Criterion | Verification | Evidence channel | Observable |
|---|---|---|---|---|
| SC1 | `ConfigEntryRecord`, `ConfigAuditEntryRecord`, `UpsertEntryRequest` expose `IsWriteOnly` (default `false`). The build stays zero-warning. | `dotnet build src/DbConfig.slnx` | build-output | Exit 0, 0 warnings. |
| SC2 | Upserting with `IsWriteOnly=true` persists the flag, forces `IsSecret=true`, and stores ciphertext. Verified on SQL Server and PostgreSQL. | `SqlServerStoreWriteOnlyTests`, `PostgreSqlStoreWriteOnlyTests`, `InMemoryStoreWriteOnlyTests` | test-run | All pass on net8.0 and net10.0. |
| SC3 | An audit row has `IsWriteOnly=true` when the old **or** new state of the entry is write-only (insert, update, downgrade, delete). Verified on both engines. | Same store test classes | test-run | All pass. |
| SC4 | The migrator adds the new columns to an existing pre-feature database (column dropped, then migrator re-run) on both engines. Re-running it is a no-op. | `*StoreWriteOnlyTests.Migrate_ExistingSchemaWithoutColumn_AddsColumn` | test-run | Pass on both engines. |
| SC5 | `GET /{scope}/{env}/{key}` and `GET /` return `value: null` for write-only entries. A plain `IsSecret` entry still returns plaintext. | `Http/WriteOnlyEndpointTests` | test-run | All pass. |
| SC6 | `GET /{scope}/{env}/audit/{key}` and `GET /audit` return `oldValue`/`newValue` null for write-only audit rows. Other rows are unchanged. | `Http/WriteOnlyEndpointTests` | test-run | All pass. |
| SC7 | A `PUT` with `isWriteOnly:false` on a write-only entry replaces the value and never returns the old one. | `Http/WriteOnlyEndpointTests.Put_DowngradeWriteOnly_ReplacesValueWithoutRevealingOld` | test-run | Pass. |
| SC8 | End to end, on both engines: a write-only `PUT` is visible to the host through `IConfiguration` in plaintext, while HTTP `GET` returns null. | `E2E/WriteOnlyRoundTripTests`, `E2E/WriteOnlyRoundTripPostgreSqlTests` | test-run | Pass. |
| SC9 | UI: create/edit support the write-only flag; the table, tree and history show a non-revealable placeholder; bulk Move/Toggle skip write-only entries; Export omits them; Import preserves `isWriteOnly` on collision overwrite. | `npm run build` in `ui/` (tsc + vite) and a manual demo-mode check | build-output | `npm run build` exits 0. Demo mode shows the write-only entry with no reveal button. |
| SC10 | Docs describe write-only, its redaction rules, and the trust model. They state that it is not an authorization boundary (§1.4). | Review of the `website/docs` diff | script-output | `grep -rl "IsWriteOnly\|isWriteOnly" website/docs` lists encryption.md, endpoints.md, editing-entries.md, bulk-operations.md, import-export.md, releases.md, migrations.md. |
| SC11 | The full suite is green with no regressions against the baseline. | `src/tests/DbConfig.Tests/bin/Debug/net8.0/DbConfig.Tests.exe` (and net10.0) | test-run | Failed count = 0 on both TFMs. |

## Architecture and design

### Core (`DbConfig.Core`)

- `ConfigEntryRecord`: append `bool IsWriteOnly = false` as the last positional parameter.
  Existing `new ConfigEntryRecord(...)` call sites still compile. The binary signature changes,
  which is acceptable pre-1.0 and goes in the release notes.
- `ConfigAuditEntryRecord`: append `bool IsWriteOnly = false`. The XML doc explains the
  "old OR new is write-only" rule.
- `InMemoryConfigStore`: on upsert, normalize `IsSecret = IsSecret || IsWriteOnly`, persist
  `IsWriteOnly`, and set the audit row's `IsWriteOnly` from `existing?.IsWriteOnly || entry.IsWriteOnly`.
  The delete audit carries `existing.IsWriteOnly`.
- `SecretDecryptionView` and `DbConfigConfigurationProvider` are **unchanged**. They key off
  `IsSecret`, which is always true for write-only rows. This is the reason for normalizing instead
  of encrypting on `IsSecret || IsWriteOnly` at every decrypt site.

### EF Core (`DbConfig.EntityFrameworkCore`)

- `ConfigEntry.IsWriteOnly` and `AuditEntry.IsWriteOnly` (bool, default false).
- `DbConfigDbContext`: map both properties the way `IsSecret` is mapped (`:70`, `:117`).
- `EfCoreConfigStore`: add `x.IsWriteOnly` to every `new ConfigEntryRecord(...)` projection
  (about 10 sites, all in this file). Normalize on upsert. Set the audit flag as in Core.
- `EfCoreAuditAppender` copies the flag. `EfCoreConfigAuditStore` projects it (3 query methods).

### Providers (hand-written SQL, §8.3)

- `InitialCreate.sql` (both engines): add the column to both `CREATE TABLE` statements, plus an
  idempotent guarded `ALTER TABLE ... ADD` for databases created before this version:
  - SQL Server: `IF COL_LENGTH(N'[{schema}].[ConfigEntries]', N'IsWriteOnly') IS NULL ALTER TABLE ... ADD [IsWriteOnly] bit NOT NULL CONSTRAINT [DF_ConfigEntries_IsWriteOnly] DEFAULT 0;`
    and the same for `AuditEntries`.
  - PostgreSQL: `ALTER TABLE "{schema}"."config_entries" ADD COLUMN IF NOT EXISTS "is_write_only" boolean NOT NULL DEFAULT false;`
    and the same for `audit_entries`.
  - Amendment (approved 2026-10-21): the `CREATE TABLE` statements are left unchanged. The column is
    added **only** by the guarded `ALTER`, so fresh and upgraded databases go through the same
    statement.
- No migrator C# changes. The script is already re-applied idempotently at startup.

### Http (`DbConfig.Http`), the enforcement point

- `UpsertEntryRequest`: append `bool IsWriteOnly = false`. `UpsertEntryEndpoint` passes it through.
- New `internal static class WriteOnlyRedaction` (`Endpoints/WriteOnlyRedaction.cs`):
  - `ConfigEntryRecord Redact(ConfigEntryRecord e)` → `e.IsWriteOnly ? e with { Value = null } : e`
  - `ConfigAuditEntryRecord Redact(ConfigAuditEntryRecord a)` → `a.IsWriteOnly ? a with { OldValue = null, NewValue = null } : a`
- Applied at every value-serializing site `[COVERAGE:4]`:
  - `GetEntryEndpoint.cs:58`
  - `QueryEntriesEndpoint.cs:85`
  - `GetAuditHistoryEndpoint.cs:57`
  - `QueryAuditEndpoint.cs:102`

  (The `:31`, `:58` and `:87` serializations write error bodies or empty arrays, with no values.
  `UpsertEntryEndpoint` and `DeleteEntryEndpoint` return 204. `DbConfig.Ui` serializes no entries.)
- Redaction lives in the Http layer, not the store. `IConfigStore` / `IConfigQuery` are in-process
  and trusted (§1.4), and the provider needs the plaintext.

### UI (`ui/`)

- `api/entries.ts`: add `isWriteOnly` to `ConfigEntry`, `ConfigAuditEntry`, `UpsertEntryRequest`, and an optional `isWriteOnly` param on `upsertEntry`.
- `store/entriesStore.ts` (amendment, approved 2026-10-21): `UpsertCoords` gains an optional `isWriteOnly`, and `upsert` passes it to `upsertEntry`. Without it, the dialogs' flag is dropped.
- `SecretValueCell.tsx`: new `isWriteOnly` prop. It renders a lock icon and "write-only" with no reveal button.
- `EntriesTable.tsx`, `EntriesTreeView.tsx`: pass `isWriteOnly`.
- `CreateEntryDialog.tsx`: "Write-only" checkbox. Checking it also checks and disables "Secret".
- `EditValueDialog.tsx`: for a write-only entry, the value field starts empty ("Enter a new value; the current value cannot be shown"), and the write-only checkbox is shown so it can be cleared.
- `EntryHistoryDialog.tsx`, `ValueDiff.tsx`, `pages/AuditLogPage.tsx`: write-only rows show "(write-only, hidden)" with no diff and no reveal.
- `BulkActionsToolbar.tsx`: `executeToggleSecret` / move throw `Error('Write-only entry: re-enter its value to change it')` for write-only entries, so `BulkOperationDialog` lists them as failed with that reason. Delete still works.
- `lib/appsettings.ts` + `ExportButton.tsx`: export filters out write-only entries, and the button shows "N write-only entries were omitted".
- `ImportDialog.tsx`: when overwriting an existing key that is write-only, send `isWriteOnly: true` so an import does not quietly downgrade it.
- `demo/data.ts`, `demo/adapter.ts`: add `isWriteOnly`, one seeded write-only entry, and the same redaction in the demo adapter.

### Docs and rules

- `website/docs/configuration/encryption.md`: new "Write-only secrets" section covering the trust
  model. It says plainly that write-only is not an authorization boundary: any app whose host
  includes the scope reads it in-process, and DB admins with the key ring can decrypt it.
- `website/docs/http-api/endpoints.md`: `isWriteOnly` in the request and response, and the redaction rules.
- `website/docs/ui-editor/editing-entries.md`, `bulk-operations.md`, `import-export.md`: the UI behavior.
- `website/docs/operations/migrations.md`: the new columns and the automatic `ALTER` on upgrade.
- `website/docs/releases.md`: an unreleased entry noting the binary-breaking record constructors.
- `.claude/rules/project-specific.md` §8.2: amend "HTTP GET responses always return plaintext" to add the write-only exception.

## Security and compliance impact

`security_impact: secrets-change`. The change touches secret storage (schema, store normalization),
the HTTP read surface (redaction), and audit rows (new flag, written in the **same**
`SaveChangesAsync` as the mutation, per §0.5. The appender path is unchanged).

- No new auth path. Redaction applies regardless of the authorization configuration.
- Read audits are unchanged. They already write `IsSecret:false` with null values.
- Values are never logged (§1.2). The new code logs nothing.

## Change manifest

See the JSON sidecar `docs/specs/2026-10-21-write-only-secrets.json` (same entries).

## Test manifest

| Path | Covers |
|---|---|
| `src/tests/DbConfig.Tests/Core/InMemoryStoreWriteOnlyTests.cs` (create) | SC2, SC3 |
| `src/tests/DbConfig.Tests/SqlServer/SqlServerStoreWriteOnlyTests.cs` (create) | SC2, SC3, SC4 |
| `src/tests/DbConfig.Tests/PostgreSql/PostgreSqlStoreWriteOnlyTests.cs` (create) | SC2, SC3, SC4 |
| `src/tests/DbConfig.Tests/Http/WriteOnlyEndpointTests.cs` (create) | SC5, SC6, SC7 |
| `src/tests/DbConfig.Tests/E2E/WriteOnlyRoundTripTests.cs` (create) | SC8 |
| `src/tests/DbConfig.Tests/E2E/WriteOnlyRoundTripPostgreSqlTests.cs` (create) | SC8 |

## Implementation batches

1. **Core + schema + store.** Records, in-memory store, EF entities/context/store/audit, both SQL
   scripts, and the in-memory + both engine store tests. Checkpoint: build, then the Unit,
   SqlServer and PostgreSql categories.
2. **Http enforcement.** Request contract, `WriteOnlyRedaction`, the 4 endpoints, Http unit tests,
   and both E2E classes. Checkpoint: build, then the full suite.
3. **UI.** All `ui/` files. Checkpoint: `npm run build`, then a demo-mode smoke check.
4. **Docs and rules.** `website/docs/*` and the project-specific.md §8.2 amendment. Checkpoint:
   the SC10 grep, then the full suite one more time.

## Risks and assumptions

- [VERIFIED:src/core/DbConfig.Http/Endpoints/UpsertEntryEndpoint.cs] `PUT` is a full replace, so the downgrade cannot leak the old value.
- [VERIFIED:src/core/DbConfig.Core/SecretDecryptionView.cs] Decryption keys off `IsSecret` only. Normalizing `IsSecret=true` keeps it unchanged.
- [VERIFIED:ui/src/components/BulkActionsToolbar.tsx] Bulk Move/Toggle re-send `entry.value` from the client. Without the skip they would null out write-only values.
- [COVERAGE:4] The Http redaction covers every value-serializing endpoint (list in Architecture → Http).
- Risk: binary break of the `ConfigEntryRecord` / `ConfigAuditEntryRecord` constructors for consumers compiled against an older version. Mitigation: release note. The project is pre-1.0 (v0.15).
- Risk: a third-party `IConfigStore` that ignores `IsWriteOnly` would return it as false, and the Http layer would then not redact. Mitigation: documented in endpoints.md. Both shipped stores implement it.
- Risk (pre-existing, not fixed here): an update that flips `IsSecret` true → false writes an audit row whose `OldValue` is ciphertext with `IsSecret=false`. Out of scope. It is noted because write-only audit rows avoid it: their redaction nulls both values.
- Risk: pre-existing Playwright screenshots may drift because of the new demo entry. Screenshots are not regenerated in this change.

## Rejected alternatives

- trap: mask in the UI only. It is cosmetic, and the network tab still shows the plaintext.
- trap: redact inside `IConfigStore`. That breaks the configuration provider and in-process readers, which legitimately need the plaintext.
- trap: encrypt on `IsSecret || IsWriteOnly` at every decrypt site. That spreads the change into `SecretDecryptionView` and the audit decrypt paths. Normalizing at write time is one rule in two stores.
- trap: a server-side bulk move endpoint. Rejected by the engineer and by lesson L28.

## Requirements

- The system shall persist an `IsWriteOnly` flag per configuration entry, defaulting to false.
- When an entry is upserted with `IsWriteOnly` true, the system shall store it with `IsSecret` true and the value encrypted through `IConfigEncryptor`.
- When the entry GET or the flat query endpoint returns an entry whose `IsWriteOnly` is true, the system shall return `value` as null.
- When an audit endpoint returns an audit row whose `IsWriteOnly` is true, the system shall return `oldValue` and `newValue` as null.
- When a mutation changes an entry whose previous or new state is write-only, the system shall write its audit row with `IsWriteOnly` true in the same `SaveChangesAsync` as the mutation.
- While an entry is write-only, the system shall return its plaintext value to in-process readers (`IConfiguration`, `IConfigStore`).
- If the migrator runs against a schema that lacks the write-only columns, then the system shall add them with default false and leave existing rows unchanged.
- If a UI bulk Move or Toggle IsSecret operation includes a write-only entry, then the system shall leave that entry unchanged and report it as failed with a reason.
- When the UI exports entries, the system shall omit write-only entries from the file and report how many were omitted.

## Constitution Check

The digest script found no bold rule ids in AGENTS.md (the ids there are `**§0.N — …**`), so the
rules below are cited by hand.

- **§0.1 / §7.5** (no push without approval): the run ends at "ready for review", with no push.
- **§0.2** (never block on first load): no change to the first-load path. The provider is untouched.
- **§0.3** (the UI never reaches the DB): all UI changes go through the existing HTTP API.
- **§0.4** (scopeFilter writes): no new endpoints. The existing scope filters are unchanged.
- **§0.5** (audit in the same SaveChanges): the new audit flag rides on the existing appender in the same context (`EfCoreConfigStore.UpsertAsync` / `DeleteAsync`). No fire-and-forget.
- **§1.2** (no values in logs): the new code adds no logging.
- **§1.3 / §8.2** (IsSecret drives encryption): write-only normalizes to `IsSecret=true`, so encryption keeps a single driver. §8.2 is amended for the HTTP plaintext exception.
- **§1.4** (IsSecret is not an authorization boundary): the same holds for write-only, and the docs say so explicitly.
- **§8.3** (hand-written SQL, both scripts): both `InitialCreate.sql` scripts get the change, with idempotent guards.
- **§4.2** (dual-engine tests): the store and E2E classes are written for both engines.
- **L28** (client-side bulk loops): kept. No bulk endpoint.

## Readiness notes

- Dependencies: no new packages.
- Rollback: revert the commit. The added columns are nullable-safe, since they are `NOT NULL DEFAULT 0/false`, and older code ignores them. To drop them, run `ALTER TABLE ... DROP COLUMN` manually. Write-only rows written meanwhile remain `IsSecret=true` ciphertext, which older versions decrypt and show as ordinary secrets.
- Test waivers: the `ui/` files have no unit runner (testing.md §4.13). They are covered by the HTTP tests (the server-side contract) plus `npm run build` and a demo-mode check. The docs and rules files need no tests.
- Lessons consulted: L28 (bulk ops as client loops). No other lesson applies.

## Review amendments (2026-10-21, approved by engineer)

Stage 1 and Stage 2 review findings changed parts of the design above. Where this section and
the earlier text disagree, this section wins.

- **`UpsertEntryRequest.IsWriteOnly` is `bool?`.** Omitted (or `null`) keeps the existing
  entry's flag; explicit `false` downgrades. Import and bulk Move/Toggle omit it.
- **`PUT` guards.** `400` when `value` is `null` over a currently write-only entry. `500`
  (and an error log) when the request makes an entry write-only but the store does not persist
  the flag. `IsSecret` is forced at the endpoint as well as in both stores.
- **Audit current-key rule.** `ConfigAuditEntryRecord.IsWriteOnly` is also true for rows whose
  key is write-only at read time. The EF audit store computes this with a correlated `EXISTS`,
  and the in-memory audit store does it through a callback into its owning store. The HTTP layer
  redacts on the row flag only. `EndpointRouteBuilderExtensions.cs` stays unchanged.
- **Audit encryption on upgrade.** When an update turns a plain entry secret, the store
  encrypts the old value for the audit row too. Before this, reading that history failed.
- **UI guards.** Edit and Create block an empty save whenever the entry (or the duplicate's
  source) is or becomes write-only. Ticking write-only on an existing entry clears the value
  (rotate on upgrade).
- **PostgreSQL DDL.** The `ADD COLUMN` is wrapped in an `information_schema` check inside a
  `DO` block, so an already-upgraded database takes no ACCESS EXCLUSIVE lock at startup.
- **Docs and rules.** Decision D8 (guarded `ALTER`), `architecture.md` §2.12/§2.13,
  `security.md` §1.3, `project-specific.md` §8.2/§8.3/§8.8 and `DbConfig.Http/CLAUDE.md`.
  `migrations.md` documents the rolling-deploy race and the migrator timeout.

## Out of scope

- Server-side move/copy endpoint.
- A global `SecretsWriteOnlyByDefault` option.
- Regenerating Playwright screenshots.
- Fixing the pre-existing `IsSecret` flip audit ciphertext issue.

## Open questions

None.

## Prior Work Check

No existing write-only, redaction or `hasValue` concept in `src/` or `ui/src/`
(`grep -rn -i "writeonly\|redact" src ui/src`: no production matches). Verdict: CLEAR.
