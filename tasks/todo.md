# db-config — TODO

## v0.1.0 — Shipped

Spec: [`docs/specs/2026-05-16-v0.1.0-roadmap.md`](../docs/specs/2026-05-16-v0.1.0-roadmap.md)
Plan: [`docs/plans/2026-05-16-v0.1.0-roadmap.md`](../docs/plans/2026-05-16-v0.1.0-roadmap.md)

- [x] **Batch 1** — Core configuration provider — 16/16 tests pass
- [x] **Batch 2** — EF Core + SQL Server provider — 11 SqlServer tests
- [x] **Batch 3** — HTTP API endpoints — 27 Unit tests
- [x] **Batch 4** — React UI scaffold — npm build produces dist (191 KB JS + 9 KB CSS)
- [x] **Batch 5** — UI core features — build clean (280 KB JS)
- [x] **Batch 6** — UI embedding into NuGet — 43/43 tests
- [x] **Batch 7** — PostgreSQL provider — 11 PG tests
- [x] **Batch 8** — Demo app wireup — suite 54/54
- [x] **Batch 9** — E2E integration tests — 5 E2E; full suite 59/59
- [x] Stage 1 review (compliance) — 2 Critical + 2 High + 4 Medium → fixed Round 1
- [x] Stage 2 reviews (test + architecture) — Round 2 fixes
- [x] Final state: **65/65 tests pass**, build analyzer-clean, lessons captured

## v0.2.0 — Architecture Refinement

Spec: [`docs/specs/2026-05-17-v0.2.0-architecture-refinement.md`](../docs/specs/2026-05-17-v0.2.0-architecture-refinement.md)
Plan: [`docs/plans/2026-05-17-v0.2.0-architecture-refinement.md`](../docs/plans/2026-05-17-v0.2.0-architecture-refinement.md)

- [x] **B10** — `IConfigStore.GetAsync` + targeted reads — 77/77 (+12)
- [x] **B11** — Two-call `AddDbConfig` DI redesign — 81/81 (+4)
- [x] **B12** — Extract `Moberg.DbConfig.EntityFrameworkCore` package — 81/81 (refactor; Core lost EF Core Relational dep)
- [x] **B13** — `IUniqueConstraintDetector` strategy — 83/83 (+2)
- [x] **B14** — Rewrite `.claude/rules` + CLAUDE.md table + README — 83/83 (docs only)

### Post-Implementation Review (v0.2.0)

- [x] Spec drift check — clean (sidecar `completed_batches` populated)
- [x] Combined compliance + architecture review — PASS (0 Critical, 2 High, 6 Medium, 2 Low)
- [x] Fix Round (M-1, M-3, M-5, M-6, H-2, L-1) — 84/84 tests pass after fixes
- [x] H-1 (second DI container) — **FIXED in v0.3.0 single-call refactor** — `BuildServiceProvider()` eliminated; `builder.AddDbConfig(b => ...)` is now the sole entry point on `IHostApplicationBuilder`

## v0.3.0 — Single-call refactor (2026-05-17)

- [x] Replace two-call DI with single `builder.AddDbConfig(b => ...)` on `IHostApplicationBuilder`
- [x] `ConfigurationBuilderExtensions.cs` + `ServiceCollectionExtensions.cs` deleted
- [x] `HostApplicationBuilderExtensions.cs` added in `DbConfig.EntityFrameworkCore` (namespace `DbConfig.Core` for ergonomics)
- [x] Polling store + HTTP store now distinct `EfCoreConfigStore` instances against same DB — no shared in-process state
- [x] Demo + E2E fixtures updated; shape tests rewritten
- [x] 85/85 tests pass after refactor
- [x] Build analyzer-clean
- [x] Lessons appended to `tasks/lessons.md`

## v0.4.0 — Shared Scopes

Spec: [`docs/specs/2026-05-17-v0.4.0-shared-scopes.md`](../docs/specs/2026-05-17-v0.4.0-shared-scopes.md)

- [x] **B15** — Core: IncludeScopes + scoped IConfigStore methods + polling merge — 94/94 (+9)
- [x] **B16** — EF Core: scoped query implementations + dual-DB integration tests — 108/108 (+14)
- [x] **B17** — HTTP API: ?includeScopes query string on list endpoint — 114/114 (+6)
- [x] **B18** — MapDbConfigHttp scopeFilter overload — 122/122 (+8)
- [x] **B19** — UI: scope badge + view filter + per-row write guard — 122/122 (UI build clean)
- [x] **B20** — Docs: README, architecture.md, project-specific.md, CLAUDE.md, lessons, todo, sidecar — 122/122 (docs only)

## v0.5.0 — Production Hardening

Spec: [`docs/specs/2026-05-17-v0.5.0-production-hardening.md`](../docs/specs/2026-05-17-v0.5.0-production-hardening.md)

- [x] **B21** — Encryption: IConfigEncryptor + DataProtectionConfigEncryptor + store integration — 145/145 (+51 from 94/94 baseline incl. v0.4.0 fixes)
- [x] **B22** — Audit log Core + EF + in-transaction writes + migrations — 171/171 (+26)
- [x] **B23** — Audit log HTTP endpoint — 180/180 (+9)
- [x] **B24** — Audit log UI: History dialog — 180/180 (UI build clean; no new .NET tests)
- [x] **B25** — Collation fix: case-sensitive scope columns — 196/196 (+16)
- [x] **B26** — Cleanup: ScopeSelector setTimeout + SpinWait test patterns — 196/196 (refactor; test count unchanged)
- [x] **B27** — Docs: README + rules + CLAUDE.md + lessons + todo + sidecar — 196/196 (docs only)

## v0.6.0 — Ergonomics

Spec: [`docs/specs/2026-05-17-v0.6.0-ergonomics.json`](../docs/specs/2026-05-17-v0.6.0-ergonomics.json)

- [x] **B28** — Read auditing (opt-in) — 218/218 (+22)
- [x] **B29** — UI diff view in history dialog — 218/218 (UI build clean; no new .NET tests)
- [x] **B30** — UI bulk edit — 218/218 (UI build clean; no new .NET tests)
- [x] **B31** — UI import/export — 218/218 (UI build clean; no new .NET tests)
- [x] **B32** — Type-mapped IConfigEncryptor (deferred decryption) — 224/224 (+6)
- [x] **B33** — v0.6.0 docs — 224/224 (docs only)

## v0.7.0 — Docs Site + UI Screenshot Tests

Spec: [`docs/specs/2026-05-18-v0.7.0-docs-and-screenshots.md`](../docs/specs/2026-05-18-v0.7.0-docs-and-screenshots.md)
Plan: [`docs/plans/2026-05-18-v0.7.0-docs-and-screenshots.md`](../docs/plans/2026-05-18-v0.7.0-docs-and-screenshots.md)

- [x] **B34** — Docusaurus 3.10 scaffold at `website/`
- [x] **B35** — UI demo mode (Vite `--mode demo` + `?demo` query + in-memory adapter)
- [x] **B36** — Playwright screenshot tests producing 10 PNGs
- [x] **B37** — Real content for all doc pages with embedded screenshots
- [x] **B38** — README docs link + lessons L29-L31 + sidecar populate + §8.13

## v0.8.0 — UI Polish (Dark mode + Tree view + Larger modals)

Spec: [`docs/specs/2026-05-18-v0.8.0-ui-polish.md`](../docs/specs/2026-05-18-v0.8.0-ui-polish.md)
Plan: [`docs/plans/2026-05-18-v0.8.0-ui-polish.md`](../docs/plans/2026-05-18-v0.8.0-ui-polish.md)

- [x] **B47** — Dark mode foundation + Docusaurus toggle fix
- [x] **B48** — Hierarchical tree view (Flat | Tree toggle)
- [x] **B49** — Larger modals (Dialog `size` prop)
- [x] **B50** — Screenshot regen (22 PNGs: light + dark for 11 states including tree view)
- [x] **B51** — Docs + lessons L32-L34 + sidecar populate + todo tick

## v0.9.0 — Per-Request Multi-Tenant

Spec: [`docs/specs/2026-05-18-v0.9.0-multi-tenant.md`](../docs/specs/2026-05-18-v0.9.0-multi-tenant.md)
Plan: [`docs/plans/2026-05-18-v0.9.0-multi-tenant.md`](../docs/plans/2026-05-18-v0.9.0-multi-tenant.md)

- [x] **B52** — Core MT abstractions (accessor + reader + options interfaces)
- [x] **B53** — Schema migration (TenantId column on both tables)
- [x] **B54** — Store layer tenant-aware methods
- [x] **B55** — Polling provider exposes ITenantConfigReader
- [x] **B56** — TenantAwareOptions<T> + AddTenantAwareOptions DI
- [x] **B57** — HTTP API tenant support (?tenantId=)
- [x] **B58** — UI tenant support (ScopeSelector Tenant field, Tenant column, dialogs)
- [x] **B59** — Demo project tenant middleware example
- [x] **B60** — Screenshots + docs + lessons L35-L37 + CLAUDE.md §0.8 + sidecar (initial; rewritten in B63)
- [x] **B62** — Refactor: replace ITenantAwareOptions design with ITenantResolver + standard IOptionsSnapshot
- [x] **B63** — Rewrite all v0.9.0 docs to match the new design
- [x] **B64** — Composed IncludeScopes × Tenants resolution in DbConfigConfigurationProvider (290 tests)
- [x] **B64.1** — Unit-test hardening: matrix-test design pattern (Theory + MemberData) for the 4-bucket cube
- [x] **B65** — Docs: resolution-order.md + multi-tenant.md/scopes.md updates + architecture.md §2.16 + lessons L38-L40 + sidecar

## Deferred to v0.10.0+

- Audit log retention pruner (`UseDbConfigAuditPruning(TimeSpan)` opt-in IHostedService)
- Multi-AddDbConfig per host (multiple scopes per process)
- Lazy per-tenant loading (currently we load ALL tenants into memory)
- Parent/inheritance scope column (v0.4.0 Option C)
- Visibility tags on entries (v0.4.0 Option D)
- Custom Dialog `role="dialog"` for a11y
- Docs hosting deployment (GH Pages / Netlify)
- Mock `Date.now()` for pixel-perfect screenshots
- Tree-view drag-and-drop re-keying
- Tenant resolution middleware (currently host-owned; could ship a configurable helper) — NEW from v0.9.0
- UI: cross-tenant copy/move workflow — NEW from v0.9.0
- Override `GetChildKeys` to expose the current tenant's keys to `IConfiguration.Bind` — NEW from v0.9.0/B65 (perf + leak-risk tradeoff documented)
- Runtime warning when a tenant-aware type is resolved via `IOptions<T>` (instead of `IOptionsSnapshot<T>`) — NEW from v0.9.0
