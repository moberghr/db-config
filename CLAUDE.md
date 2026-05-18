# DbConfig — Engineering Standards

> Database-backed `IConfiguration` provider for .NET 8 with embedded React editor UI. Provider-agnostic core + SQL Server / PostgreSQL providers. Ships as `Moberg.DbConfig.Core`, `Moberg.DbConfig.Http`, `Moberg.DbConfig.Ui`, `Moberg.DbConfig.Provider.SqlServer`, `Moberg.DbConfig.Provider.PostgreSql`.
>
> Sister project to **Warp** (`C:\Users\DomagojMedo\source\repos\jobly`). Engineering standards, analyzer config, build hygiene, and review process are inherited from warp.
>
> Source of truth for AI agents: this file + `.claude/rules/`. Reference docs in `.claude/references/`.

---

## Critical Rules (Always Apply)

- **§0.1 — NEVER push to remote without explicit approval.** Even with green CI. The engineer reviews the diff first.

## Known Limitations (v0.1.0+)

- **Direct SQL `DELETE` on the `DbConfig_Entries` table will not be reflected by the polling provider until another row's `ModifiedUtc` advances.** Always mutate via the API — the HTTP `DELETE`/`PUT` endpoints fire the in-process reload signal. Direct DB writes from migrations or DBA tools are not first-class in v0.1.0.
- **§0.2 — Encryption: per-entry via `IsSecret` flag, default ASP.NET Data Protection.**
  Entries with `IsSecret = true` are encrypted at rest using `IConfigEncryptor`. The
  default impl wraps `IDataProtector` (filesystem key ring, ephemeral by default).
  Consumers can register a custom encryptor via either instance (`AddSingleton<IConfigEncryptor>(instance)`)
  or type-mapped (`AddSingleton<IConfigEncryptor, MyImpl>()`) before `AddDbConfig`.
  Optionally enable read auditing via `DbConfigOptions.AuditReads = true` for
  compliance trails ("who read which secret"). The UI access-warning banner remains —
  "Configuration values may be visible to anyone with database access if they are
  not marked IsSecret."
- **§0.3 — Authorization is opt-in via `DbConfigUiOptions`.** The package does not
  require any auth: by default `MapDbConfigUi` and `MapDbConfigHttp` are open. Three
  built-in options on the UI side (since v0.10.0):
  - `opts.UseBuiltInLogin<TValidator>()` — wires the package's cookie scheme + `/login`
    form; consumer implements `IDbConfigCredentialValidator` and registers it in DI
    (typically scoped) before calling `MapDbConfigUi`.
  - `opts.UnauthorizedRedirectUrl = "/my-login"` — redirect browser requests to the
    consumer's own login page (combine with an `IDbConfigAuthorizationFilter` that
    checks the consumer's auth state).
  - `opts.Authorization = new MyFilter()` — any `IDbConfigAuthorizationFilter` impl
    (header check, IP allowlist, custom JWT). The package ships
    `LocalRequestsOnlyAuthorizationFilter` for dev/demo use.

  Hosts can still skip all of this and chain `.RequireAuthorization("policy")` on the
  returned `RouteGroupBuilder` to compose with an existing ASP.NET Core auth pipeline —
  the v0.9.0 pattern continues to work. `MapDbConfigHttp` still has no built-in
  auth surface; it remains host-owned via `.RequireAuthorization(...)`.
- **§0.4 — NEVER block on the configuration provider's first load.** ASP.NET Core builds the configuration system synchronously at host construction; the DB store MUST tolerate transient unavailability with a clear, in-process exception (`InvalidOperationException` with the connection details redacted) rather than hanging or silently returning empty values.
- **§0.5 — NEVER let the React UI reach the database directly.** All UI traffic goes through `Moberg.DbConfig.Http`'s JSON endpoints, even in the demo host. The store abstraction is a server-only surface.
- **§0.6 — NEVER write to a scope outside your `scopeFilter` from the same host.** When
  `MapDbConfigHttp` is configured with `scopeFilter: "X"`, writes to other AppNames return
  403. Use a separately-deployed admin host (or a separate group with `PlatformAdmin` policy)
  to mutate shared scopes. Don't bypass the filter by registering both groups under the same
  auth policy — that defeats the separation.
- **§0.7 — Audit writes are in-transaction with mutations.** Every Upsert/Delete on
  `EfCoreConfigStore` writes a `DbConfig_AuditEntries` row in the SAME `SaveChangesAsync`
  as the mutation. NEVER refactor this to fire-and-forget — losing audit rows breaks
  compliance posture. Audit log retention is the consumer's responsibility (no built-in
  pruner; document manual cleanup). Audit row `Action` MUST reflect the DB state transition
  at `SaveChangesAsync` time, not the caller's intent. A losing writer in a concurrent-insert
  race emits an `Update` audit row because the row exists by the time their save runs — that
  is correct. Read audit writes (§2.14) are fire-and-forget by design — this is an
  intentional exception that prioritizes GET throughput over zero audit-row loss. Mutation
  audits MUST remain in-transaction.
- **§0.8 — Tenant context is defined by `ITenantResolver.Resolve()`.** Consumers
  implement `ITenantResolver` to return the current tenant id from whatever source
  fits their auth model (JWT claim, header, route, subdomain). `IConfiguration[key]`
  reads consult the resolver each call: non-null tenant id selects the tenant-specific
  entry (with fallback to global); null returns global. **`IOptions<T>` is singleton-cached
  and binds once at startup with no request context → always gets global config.
  For tenant-aware types use `IOptionsSnapshot<T>` (scoped per-request) which rebinds
  with the resolver's current tenant.** Document this constraint loudly.

---

## Skill Routing

| What you need | Skill | When |
|---|---|---|
| Build a feature | `/mtk <description>` | New endpoints, providers, UI screens, multi-file work |
| Quick fix | `/mtk fix <description>` | Bug fixes, 1–3 file changes |
| Pre-commit check | `/mtk review before commit` | Before every commit |

---

## Tech Stack

- **Active stack:** dotnet (`net8.0`)
- **Build:** `dotnet build src/DbConfig.slnx`
- **Test (all):** `dotnet test src/tests/DbConfig.Tests/DbConfig.Tests.csproj` (Microsoft Testing Platform runner via `UseMicrosoftTestingPlatformRunner=true`)
- **Test (filtered by category):** `... -- --filter-trait "Category=<name>"` (categories defined per fixture; see `testing.md`)
- **Format:** `dotnet format --verbosity quiet`
- **Frontend (from `ui/`):** `npm install && npm run dev` — Vite + React, embedded in `Moberg.DbConfig.Ui` NuGet at build time via the `BuildUI` MSBuild target.

For framework-specific guidance, see `.claude/skills/tech-stack-dotnet/SKILL.md` (in plugin cache).

---

## Project Profile

- **Framework:** .NET 8 (LTS)
- **Solution:** `src/DbConfig.slnx` — 7 projects across `core/`, `core/providers/`, `tests/`, `demo/`
- **Data layer:** EF Core 8 (SQL Server via `Microsoft.EntityFrameworkCore.SqlServer`, PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`)
- **Patterns:** Standard ASP.NET Core configuration extensibility (`IConfigurationSource`, `IConfigurationProvider`, `IChangeToken`). Pluggable `IConfigStore` abstraction so the EF Core implementation is one of potentially many.
- **Reload:** Polling-based (configurable interval, default 30s); store implementations expose a cheap "has changed since timestamp" query
- **Test stack:** xUnit v3 (`xunit.v3.mtp-v2`), Shouldly, Moq, Respawn, Testcontainers (MsSql + PostgreSql), `Microsoft.AspNetCore.TestHost` for endpoint integration tests
- **Frontend:** Vite + React + TypeScript + Tailwind + shadcn + Zustand + Axios (matches warp UI stack)
- **Analyzers (enforced as errors):** StyleCop, Roslynator, SonarAnalyzer, Meziantou (`TreatWarningsAsErrors=true` in `src/Directory.Build.props`)

---

## Domain Model — One-Sentence Refresher

Everything is a **ConfigEntry** uniquely identified by `(AppName, Environment, Key)`. The configuration provider polls a store on a configurable interval and fires `IChangeToken` when the highest-watermark `ModifiedUtc` advances. The HTTP layer exposes a CRUD surface over entries; the React UI is the only first-class consumer of that surface. Stores are pluggable via `IConfigStore`; EF Core is the canonical store, now extracted into `Moberg.DbConfig.EntityFrameworkCore` so `Core` carries no EF dependency. Provider-specific unique-constraint detection is handled by `IUniqueConstraintDetector` implementations in each provider package. DI uses a single-call shape: `builder.AddDbConfig(lambda)` on `IHostApplicationBuilder`. Optional `DbConfigOptions.IncludeScopes` enables multi-scope reads with explicit precedence. Full details in `.claude/rules/architecture.md`.

---

## Standards Reference

Detailed rules in `.claude/rules/` (auto-loaded by Claude Code). Generic engineering files (coding-style, git-workflow, security, testing, data-layer, performance) apply as-is unless otherwise noted.

| File | Covers | Adaptation status |
|---|---|---|
| `architecture.md` | Single-call DI, `IConfigStore`, polling provider, scope merging, package boundaries, UI embedding, multi-tenant architecture | db-config-specific, v0.9.0 (updated 2026-05-18), §2.1–§2.15 |
| `coding-style.md` | `var`, braces, LINQ chaining, naming, project-specific style | inherited from warp |
| `data-layer.md` | EF Core, no raw SQL, `AsNoTracking`, `Select` over `Include`, schema | inherited from warp |
| `git-workflow.md` | Hierarchical branches, imperative commits, analyzer-clean builds | inherited from warp |
| `performance.md` | Hot path discipline, cheap polling, signal-driven wakeup | inherited from warp |
| `project-specific.md` | Scoping, `IsSecret` flag, migrations, dual-DB testing, reload caveat, shared scopes conventions, multi-tenant conventions | db-config-specific, v0.9.0 (updated 2026-05-18), §8.1–§8.14 |
| `security.md` | Secrets, PII in logs, transactions, row locking | inherited from warp |
| `testing.md` | xUnit v3, `[TimedFact]`, fixtures, integration patterns | inherited from warp |

Reference docs (read on-demand by skills and review agents):

- `.claude/references/architecture-principles.md` — core engineering principles (curated; warp-derived, mostly generic)
- `.claude/references/coding-guidelines.md` — Moberg C# coding style
- `.claude/references/quick-check-list.md` — reviewer fast-check list
- `.claude/references/pre-commit-review-list.md` — pre-commit security review checklist

---

## Build & PR Hygiene

- **Branches:** hierarchical with `/` (`feat/`, `fix/`, `chore/`, `docs/`, `test/`, `bug/`).
- **Commits:** imperative mood, describe the "what". PR titles describe the user-visible change.
- **Tests on both DBs:** every new behavior asserts on both SQL Server and PostgreSQL (Testcontainers).
- **Build must be analyzer-clean** — `TreatWarningsAsErrors=true` is non-negotiable.

<!-- mtk-setup: bootstrapped manually from warp 2026-05-16 -->
