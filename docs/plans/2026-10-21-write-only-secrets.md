# Plan: Write-only secrets (`IsWriteOnly`)

Spec: `docs/specs/2026-10-21-write-only-secrets.md` (sidecar `.json`)

## Batch 1: Core, schema, stores

Files: `ConfigEntryRecord.cs`, `ConfigAuditEntryRecord.cs`, `InMemoryConfigStore.cs`,
`ConfigEntry.cs`, `AuditEntry.cs`, `DbConfigDbContext.cs`, `EfCoreConfigStore.cs`,
`EfCoreAuditAppender.cs`, `EfCoreConfigAuditStore.cs`, both `InitialCreate.sql`.
Tests: `Core/InMemoryStoreWriteOnlyTests.cs`, `SqlServer/SqlServerStoreWriteOnlyTests.cs`,
`PostgreSql/PostgreSqlStoreWriteOnlyTests.cs`.

Acceptance:
- An upsert with `IsWriteOnly=true` reads back with `IsWriteOnly=true` and `IsSecret=true`, and the raw row holds ciphertext.
- Audit rows have `IsWriteOnly=true` for insert, update, downgrade (old write-only, new not) and delete of a write-only entry. A plain secret's audit row has `IsWriteOnly=false`.
- After dropping the column, re-running the migrator restores it with default false. A second run does nothing.

Checkpoint: `dotnet build src/DbConfig.slnx` with zero warnings, then the test exe with the Unit, SqlServer and PostgreSql categories.

Boundary: no Http or UI changes.

## Batch 2: Http enforcement

Files: `UpsertEntryRequest.cs`, `UpsertEntryEndpoint.cs`, `WriteOnlyRedaction.cs` (new), and the
four read endpoints. Tests: `Http/WriteOnlyEndpointTests.cs`, `E2E/WriteOnlyRoundTripTests.cs`,
`E2E/WriteOnlyRoundTripPostgreSqlTests.cs`.

Acceptance:
- Every response from the four read endpoints has a null value for write-only entries and rows, while `isWriteOnly: true` is present.
- A plain `isSecret` entry still returns plaintext.
- A downgrade `PUT` returns 204, and the old value never appears in any response.
- E2E: `IConfiguration[key]` equals the plaintext, while the HTTP `GET` value is null.

Checkpoint: build, then the full test exe on net8.0 and net10.0.

Boundary: redaction stays in the Http layer only.

## Batch 3: UI

Files: all `ui/src` entries in the manifest.

Acceptance:
- `npm run build` exits 0.
- In demo mode, the seeded write-only entry shows a lock and "write-only" with no reveal button.
- Edit opens with an empty value field.
- Bulk Toggle/Move list write-only entries as failed with the reason.
- Export omits them and shows a count.
- An import that overwrites a write-only key keeps `isWriteOnly`.

Checkpoint: `npm run build` in `ui/`, then a demo-mode smoke check.

## Batch 4: Docs and rules

Files: the seven `website/docs` pages and `.claude/rules/project-specific.md` §8.2.

Acceptance:
- The SC10 grep lists all seven pages.
- encryption.md states the trust model and that write-only is not an authorization boundary.

Checkpoint: the grep, then a final full suite run.

## Gate sequence

plan_trust_gate (approval) → batch checkpoints 1–4 → phase_exit_gate (full suite) → spec drift →
review (compliance, then test, silent-failure, architecture and context-miner lanes at HIGH) → fixes → cleanup.
