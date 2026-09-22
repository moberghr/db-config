# Decision Log (ADR-lite)

Append-only. Newest last. Each entry: what was decided, why, and what it costs.

---

## D1 — Polling with a watermark, not change-data-capture
**Decision:** the provider detects change by comparing `MAX(ModifiedUtc)` on a configurable interval
(default 30s), plus an in-process `IDbConfigReloadSignal` fired by HTTP writes.
**Why:** works identically on SQL Server and PostgreSQL with no broker, trigger, or extension.
**Cost:** a direct-SQL `DELETE` does not advance the watermark, so it stays invisible until another
row changes. Documented rather than fixed — see `project-specific.md` §8.6.

## D2 — Extract `Moberg.DbConfig.EntityFrameworkCore` as its own package (v0.3.0)
**Decision:** move `DbConfigDbContext`, `EfCoreConfigStore`, the detector abstraction, and
`AddDbConfig` out of `Core` into a transitive package both providers depend on.
**Why:** otherwise `Core` carries the EF Core transitive dependency onto every consumer — including
those writing custom non-EF stores — or the same ~500 lines get duplicated across both providers
with bug-fix drift risk.
**Cost:** one more package to publish; marked `[TRANSITIVE — do not install directly]` in its
NuGet metadata.

## D3 — Provider-specific unique-constraint detection behind `IUniqueConstraintDetector`
**Decision:** `EfCoreConfigStore` catches `DbUpdateException` and asks an injected detector whether
it was a unique-constraint violation; each provider package ships its own.
**Why:** keeps `SqlException.Number == 2627/2601` and `PostgresException.SqlState == "23505"` inside
the provider packages, so the store stays provider-agnostic.
**Cost:** a custom store must supply a detector.

## D4 — Authorization is opt-in; the host owns identity
**Decision:** ship no `[Authorize]` attributes, no policies, no authentication middleware. Offer a
built-in cookie login, an authorization-filter hook, and plain `.RequireAuthorization()` composition.
**Why:** the package cannot know the host's auth model, and guessing wrong is worse than deferring.
**Cost:** the default mount is open. Every deployment must make an explicit choice.

## D5 — Mutation audits in-transaction, read audits fire-and-forget
**Decision:** Upsert/Delete write their audit row in the same `SaveChangesAsync`; `Action=Read`
audits are fire-and-forget.
**Why:** losing a mutation audit row breaks compliance posture, but making every GET acquire a write
transaction would roughly double read latency.
**Cost:** a process crash can drop read-audit rows. Documented as an intentional asymmetry.

## D6 — `ITenantResolver` is the tenant boundary (v0.9.0)
**Decision:** tenant data is exposed through `IConfiguration[key]`, and a consumer-implemented
`ITenantResolver.Resolve()` selects the tenant on every read.
**Why:** it reuses the standard options pipeline — `IOptionsSnapshot<T>` becomes tenant-aware with no
custom options type.
**Cost:** a buggy resolver is the primary cross-tenant leak risk, and `IOptions<T>` silently returns
global values forever. Both documented loudly — AGENTS.md pointer, `architecture.md` §2.15,
`project-specific.md` §8.14.

## D7 — Multi-target `net8.0;net10.0` (v0.15.0)
**Decision:** packages and tests multi-target .NET 8 and .NET 10 to pick up EF Core 10.
**Why:** consumers on the LTS should not be forced forward to get fixes.
**Cost:** a green test run on one TFM is not a green run on both; CI and local runs must be explicit
about which executable they invoked.
