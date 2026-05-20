# Lessons — db-config v0.1.0

Captured 2026-05-16 during the implement-loop autonomous run.

---

## L1 — Dual DI registration is a design-smell to avoid

`AddDbConfig` builds its own internal `ServiceCollection` because `IConfigurationBuilder` predates the DI surface. The result: `IConfigStore` and `IDbConfigReloadSignal` are NOT in `builder.Services` for free. The demo and E2E fixture both have to either dual-register or walk `IConfigurationRoot.Providers` to bridge the reload signal back into host DI.

**Rule:** When designing extension points that sit on a pre-DI surface (`IConfigurationBuilder`, `IHostBuilder` in older patterns), think early about how downstream code reaches the things you registered. If the answer is "a second registration on the host's `IServiceCollection`", the public API is leaky.

**When this applies:** Any new `Add*` extension on a non-DI builder. Prefer:
- Returning a builder object the host can call `.AddTo(IServiceCollection)` on, OR
- Requiring the caller to invoke a second `AddXyzServices(builder.Services)` explicitly, with the docs saying so.

**Tracked for db-config v0.2.0.**

---

## L2 — Watermark-only polling cannot observe pure DELETEs

`DbConfigConfigurationProvider` polls `IConfigStore.GetLatestModifiedUtcAsync` and only reloads when the max `ModifiedUtc` advances. A DELETE removes the row, so it does NOT advance any remaining `ModifiedUtc`. Pure-DB DELETEs (someone runs SQL directly) are invisible to the provider until any other row's timestamp advances.

The API path is fine — `DeleteEntryEndpoint` calls `IDbConfigReloadSignal.Trigger()` explicitly, which forces an immediate reload that discovers the missing key.

**Rule:** Watermark-based change detection requires soft-delete OR a row-count term OR an out-of-band signal. Pick one before shipping. We chose "API is the only first-class mutation surface" — document the limitation.

**Discovered:** B9 E2E test had to insert a sentinel PUT after DELETE to advance the watermark; the workaround revealed the limitation.

---

## L3 — `.NET 8 + TestServer + Results.Ok()` hits a `PipeWriter.UnflushedBytes` NotImplementedException

ASP.NET Core 8's `Results.Ok(object)` writes JSON via `PipeWriter`, but `TestServer`'s `ResponseBodyPipeWriter` does not implement `UnflushedBytes`. Throws `InvalidOperationException` at runtime, only inside tests.

**Fix in B3:** GET endpoints write JSON directly to `Response.Body` via `JsonSerializer.SerializeAsync`. Identical output, no PipeWriter dependency.

**Fixed in .NET 9** per dotnet/aspnetcore #54370.

**When this applies:** Any minimal API GET endpoint returning a JSON object via `Results.Ok()` that is tested with `Microsoft.AspNetCore.TestHost`. Either upgrade to .NET 9+ or use the direct-stream-write workaround.

---

## L4 — xUnit v3 MTP runner: `dotnet test` shows "Zero tests ran"; direct exe works

On .NET 8 test projects with `UseMicrosoftTestingPlatformRunner=true` and a `runtimeconfig.template.json` for roll-forward, `dotnet test` may report "Zero tests ran" with exit code 5 due to an argument-passing quirk between `dotnet test` and the MTP runner. Running the test executable directly (`./DbConfig.Tests.exe`) discovers and runs the suite normally.

**Workaround:** `cd src/tests/DbConfig.Tests/bin/Debug/net8.0 && ./DbConfig.Tests.exe`. Add to `CLAUDE.md` Tech Stack notes if it persists.

---

## L5 — Hand-authored EF Core migrations across two providers

We hand-wrote migrations (Up/Down/Designer/ModelSnapshot) instead of using `dotnet ef migrations add`. This works fine but requires staying disciplined about keeping the DbContext's `OnModelCreating` and the Designer's annotations in sync.

**Rule:** Hand-authored migrations are acceptable for small entity sets. Once we have >3 entities or any complex relationship, switch to `dotnet ef`. Document the trade in `.claude/rules/data-layer.md` when it comes time to rewrite that file.

---

## L6 — Stack the warp UI deps; skip warp-specific libs

Vite + React 19 + TS 5.9 + Tailwind 4 + shadcn primitives + Zustand 5 + Axios mirrors warp. Adding `@xyflow/react`, `@dagrejs/dagre`, `chart.js`, or `@microsoft/signalr` to db-config would inflate the embedded NuGet by ~500KB with no current use.

**Rule:** When mirroring a sister project's stack, copy the base layer but explicitly omit feature-specific libs. Document which libs are intentionally absent so future contributors don't reach for them.

**Currently shipped:** ~280 KB JS + 19 KB CSS in the embedded UI. Acceptable for a NuGet.

---

## L7 — Concurrent upsert via `Task.WhenAll` exercises the retry path; sequential awaits don't

`Upsert_TwiceSameKey_LastWriterWins` runs two upserts sequentially and passes trivially — neither sees the unique-constraint violation. To actually exercise the `IsUniqueConstraintViolation` retry branch in `EfCoreConfigStore`, fire both tasks with `Task.WhenAll` so they race.

**Caught in:** Stage 2 test review (C1) flagged that PG had only the sequential test. Round 2 added the concurrent variant.

**Rule:** "Last-writer-wins" claims need concurrent test coverage. Sequential awaits prove serialization, not race resilience.

---

## L8 — Provider-specific exception sniffing in Core is technical debt

`EfCoreConfigStore.IsUniqueConstraintViolation` inspects `ex.InnerException.GetType().FullName` for `"Microsoft.Data.SqlClient.SqlException"` (Number 2627/2601) and `"Npgsql.PostgresException"` (SqlState "23505"). This is provider knowledge living in Core via string-and-reflection.

**Acceptable for v0.1.0** with only two providers. **Refactor before adding a third:** extract to an `IUniqueConstraintDetector` registered alongside each provider's `Use*` extension. Tracked for v0.2.0.

---

## L9 — Compliance review pattern: §0.2 disclosure must be in three surfaces

CLAUDE.md §0.2 said "documented in every public-facing surface" but the initial Round-1 implementation only put the disclosure on the UI banner. The Stage 1 compliance reviewer caught: README was a stub and NuGet `<Description>` fields lacked the disclosure. Round 1 fix added both.

**Rule:** When a security-relevant decision is documented as a `Never X without Y` in CLAUDE.md, audit ALL public surfaces (README, NuGet description, UI, error messages) at review time, not just the most visible one.

---

## L10 — `Phase 2.5` autonomous mode survives genuine drift without re-asking

The implement skill says autonomous mode should "halt on non-auto-fixable drift". In practice for db-config:
- B2 added a public `UseEntityFrameworkCore` method (structurally implied; sidecar amended)
- B3 added `IDbConfigReloadSignal` (planned in the batch description; sidecar amended)

Both could have triggered Phase 2.5 re-opens, but both were structurally inevitable from the spec's decomposition. Treating them as sidecar amendments (with documented justifications) kept the autonomous run moving without losing the audit trail.

**Rule:** Use the "spec ambiguity vs. surprise scope expansion" distinction when classifying drift. If the engineer reading the spec would clearly approve the addition, it's ambiguity (amend sidecar, continue). If they would push back, it's expansion (re-open 2.5, halt).

---

# v0.2.0 lessons (2026-05-17)

## L11 — `IConfigurationBuilder` pre-dates DI; bridging it is intrinsically lossy

`IConfigurationBuilder` is built BEFORE the host's `IServiceProvider` exists. Any configuration source that wants to consume host-registered services has only three options: (a) take the IServiceCollection and `BuildServiceProvider()` early (the second-container trap), (b) require a separate post-`Build()` initialization step from the consumer, or (c) push the config source out of the configuration pipeline entirely and reload from a hosted service. v0.2.0's two-call design picked (a) with lazy materialization at first `Load()`. It eliminates the inner `ServiceCollection` antipattern from v0.1.0 but inherits a different cost: services registered between `AddDbConfig` and first `Load()` may or may not be visible depending on timing.

**Rule:** When designing an `IConfigurationSource` that needs DI services, document the timing constraints loudly and recommend ordering ("register everything before the source"). Any "no caveats" claim is a lie.

## L12 — `TryAddSingleton` is the default, not `AddSingleton`, for library extensions

`UseEntityFrameworkCore` initially used `AddSingleton<IConfigStore, EfCoreConfigStore>()`, silently overriding any consumer-supplied custom `IConfigStore`. Caught in v0.2.0 Stage 1 review. The fix is one keyword: `TryAddSingleton`. Same applies to `TimeProvider`, `ILogger<T>`, and any interface a consumer might already have a registration for.

**Rule:** Library extensions that register services into the consumer's `IServiceCollection` should ALWAYS use `TryAdd*` unless the registration is something only the library owns (markers, internal-only types). `AddSingleton` in a library extension is almost always a bug.

## L13 — Sidecar `completed_batches` must be amended during the run, not at the end

v0.2.0's autonomous orchestrator dispatched all 5 batches successfully but left the sidecar's `completed_batches` array empty until the final cleanup. A future skill reading the sidecar to determine what's been done would have re-implemented everything. The orchestrator should amend the sidecar after each batch (and tick `tasks/todo.md`), not as a closing step.

**Rule:** The drift audit trail and the orchestration recovery state are the same artifact. Update both as the run progresses, not at the end. Otherwise a mid-run interruption (rate limit, crash, user pause) requires manual reconciliation to resume cleanly.

## L14 — `internal sealed` registration markers + write-once setters compose well

The `DbConfigRegistrationMarker` + `Source { get; SetSource() }` pattern lets the two-call DI keep its safety guarantees (single-write, throws on double-set) without exposing any extra surface. Both invocations of `AddDbConfig` (services + configuration) can locate each other through the marker without reflection or static state. The `internal sealed` visibility keeps it strictly an implementation detail.

**Rule:** When two extension methods need to coordinate state across calls on the same `IServiceCollection`, register an internal-sealed marker singleton. Look it up via `services.Any(x => x.ServiceType == typeof(Marker))` for guard checks; via `services.FirstOrDefault(x => x.ServiceType == typeof(Marker))?.ImplementationInstance` for content access. Don't use `[ThreadStatic]` or process-wide statics — they break parallel host construction in tests.

## L15 — Pure refactors (B12) still need test coverage updates

B12 was billed as a "pure refactor" — moving types from one assembly to another with namespace shifts. No new behavior. But ~10 test files needed `using DbConfig.Core.EntityFrameworkCore;` → `using DbConfig.EntityFrameworkCore;` edits. And two migration `.Designer.cs` files had to update the CLR type string for the entity. Pure refactor != zero test churn; account for it in batch budgeting.

**Rule:** When planning an "extract package" batch, grep the entire codebase (including migrations and Designer files) for the moved namespace BEFORE writing the plan. Add every hit to the batch file list. Underestimating this churn caused B12 to touch ~28 files instead of the originally-listed ~14.

## L16 — Compare your API to similar libraries before writing it

The user asked a sharp question after the v0.2.0 review: "AWSSecretsManagerConfigurationExtensions has only 1 call — why do we need two?" That triggered the v0.3.0 single-call refactor. The original two-call design was over-engineered: it assumed the polling provider had to resolve `IConfigStore` from the host's DI, which forced either an inner `ServiceCollection` (v0.1.0) or a `BuildServiceProvider()` second container (v0.2.0). Looking at AWSSecretsManager revealed the cleaner pattern: configuration sources can build their own backing client from the options the lambda provides. No DI lookup needed for the source's own store. The HTTP layer gets its own store instance from host DI; both stores point at the same DB. Two instances, zero shared state, zero magic.

**Rule:** Before designing an `IConfigurationSource` (or any extension point), find 2-3 popular existing implementations of the same pattern (`AWSSecretsManagerConfigurationExtensions`, `Azure App Configuration`, `Vault.Configuration`, `Etcd.Configuration`) and compare their call shapes. If yours is more complex, ask why before shipping. "One call" is a strong default — the proof burden is on adding more calls, not removing them.

## L17 — `IHostApplicationBuilder` is the right extension target for host-spanning concerns (.NET 8+)

The single-call refactor extends `IHostApplicationBuilder` from `Microsoft.Extensions.Hosting`, not `WebApplicationBuilder`. Both `WebApplicationBuilder` and `HostApplicationBuilder` (generic host / worker services) implement this interface in .NET 8+. The interface exposes everything DbConfig needs from a host in one place: `Configuration` (an `IConfigurationManager` that's source AND root), `Services`, `Environment`, `Logging`.

**Rule:** When writing host-level extensions in .NET 8+, target `IHostApplicationBuilder` unless you specifically need ASP.NET Core surface (auth, endpoint mapping, etc.). It's the broadest applicable type — works in ASP.NET Core, worker services, and any custom host. Don't extend `WebApplicationBuilder` directly unless you must.

---

# v0.4.0 lessons (2026-05-17)

## L18 — Server returns raw, ordered; clients merge

For multi-scope reads (`GetAllScopedAsync` + the `?includeScopes` HTTP query), the server
returns ALL rows from ALL listed scopes, ordered by scope position. It does NOT merge.
This forces both the polling provider and the UI to do their own merge — which they have
to anyway (polling builds a dictionary; UI wants to render "shadowed" indicators). Two
benefits: (1) the server has one canonical behavior; (2) clients can render scope-of-origin
info without a server round trip.

**Rule:** when a server returns aggregated data that consumers must transform (filter,
merge, group), prefer to return the raw ordered set and let the consumer transform. Only
collapse server-side when the wire size is the bottleneck.

## L19 — `scopeFilter` is the right shape for per-scope auth

The host-owned auth model (§0.3) is good but pushed too much to consumers when they wanted
per-scope policies — they had to write custom middleware. v0.4.0's `scopeFilter` parameter
collapses this to one optional argument on `MapDbConfigHttp`. Pattern: small additive
parameter on the mapping extension, NOT a new abstraction or middleware. ASP.NET Core's
`IEndpointFilter` makes this trivial — one filter at the group level inspects route values.

**Rule:** before introducing an abstraction for "per-X policy", check whether an optional
parameter on the existing mapping extension plus `AddEndpointFilter` covers the use case.
Most "policy per resource shape" needs do not require a new abstraction.

## L20 — Scope ordering is a contract, not an implementation detail

`IConfigStore.GetAllScopedAsync` returns entries ordered by their Scope's position in the
input list. This is a contract — the polling provider and UI both rely on it. The EF Core
implementation re-orders in memory after the SQL `IN (...)` query because SQL doesn't
preserve list order. The InMemoryConfigStore already returns in input order. **Document
the ordering invariant in the interface XML doc.** A future store implementer who returns
results in a different order will silently break precedence merging.

**Rule:** when an interface returns a sequence whose order matters, encode the order
invariant in the XML doc AND a test. "Returns entries in input scope order" is a contract,
not an implementation hint.

---

# v0.5.0 lessons (2026-05-17)

## L21 — Don't double-encrypt; capture-then-pass-through audit values

`EfCoreConfigStore` stores ciphertext in the main `Value` column. When writing an audit
row, we capture the OldValue field BEFORE the mutation (still ciphertext from the prior
write) and pass NewValue as the about-to-be-stored ciphertext. No re-encryption. The
audit row carries the same form as the main row; the reader decrypts once.

**Rule:** when an audit log captures previous/next state of an encrypted column, store
the values in their already-encrypted form. Don't decrypt-then-re-encrypt; that's
unnecessary work and a potential audit trail discontinuity if key rotation changed.

## L22 — `TryAddSingleton` for library extensions; consumers configure FIRST then call our `AddDbConfig`

Encryption made this concrete: our default `IConfigEncryptor` registration uses
`TryAddSingleton`. Consumers who need Azure Key Vault / AWS KMS / their own envelope
encryption register their `IConfigEncryptor` BEFORE `builder.AddDbConfig(...)`. The
TryAdd then becomes a no-op. Same pattern applies to `IDataProtectionProvider`
configuration (`AddDataProtection().PersistKeysToFileSystem(...)`).

**Rule:** library extensions that register infrastructure services should always use
`TryAdd*` patterns. Document the "register first, then call our extension" idiom
explicitly so consumers know how to customize. Lesson L12 (v0.2.0) already noted this;
v0.5.0 is the concrete payoff.

## L23 — Default ephemeral key rings ARE a footgun; document and recommend persistence

`DataProtectionProvider.Create("Scope")` produces an ephemeral key ring — the keys
exist for the lifetime of the process and regenerate on restart. Any encrypted data
written by a previous instance is unreadable after a restart. We default to this for
simplicity but the docs need to LOUDLY recommend `PersistKeysToFileSystem` or similar
for any non-toy deployment.

**Rule:** when a security primitive has a "works out of the box" default that creates
data loss potential, document the persistence story in the README, the rules, and in
the package description. Don't expect consumers to read the source.

## L24 — In-transaction audit ≠ async audit; pick atomicity over throughput

The choice between in-transaction audit (audit row committed with mutation) and async
audit (mutation commits; audit fires-and-forgets afterward) is a tradeoff:

- In-transaction: 2x write rows per mutation; same transaction; no missing audit rows
- Async: 1x write per mutation; audit lag; possible data loss if process crashes

For a config-management workload (low write volume, audit is the safety story), the
2x write cost is irrelevant and the "no missing audit rows" guarantee is essential.
For a high-volume log-style audit (10k mutations/sec), async-with-durable-queue is
the right tradeoff. We chose in-transaction because db-config is squarely in the
former category.

**Rule:** explicitly classify your workload before picking the audit topology. "Audit
is best-effort" is a valid choice but it should be a deliberate one, not a default.

---

# v0.6.0 lessons (2026-05-17)

## L25 — Fire-and-forget audit writes: explicit decision, not default

Read auditing uses fire-and-forget writes (`Task.Run` wrapping the audit write with
try/catch), not in-transaction. The reason: in-transaction read auditing would mean
every GET acquires a DB write transaction, doubling read latency and forcing readers
to wait on write contention. The trade is "slight chance of missing audit rows on
crash" — acceptable for compliance posture, devastating for read throughput if
in-transaction.

**Rule:** audit-vs-not is a per-action-type decision. Mutations stay in-transaction
(safety-critical). Reads go fire-and-forget (throughput-critical). Document the
classification rationale, not just the implementation.

## L26 — Deferred decryption: separate "is the value stored" from "can the consumer read it"

The v0.5.0 instance-only constraint on `IConfigEncryptor` came from coupling two
concerns: "the polling store needs an encryptor at construction time" and "consumers
need to read encrypted values from `IConfiguration`". B32 separated them — the
polling store stores ciphertext as-is; the configuration provider decrypts on `TryGet`.
The encryptor can arrive late (via hosted service) without affecting the store path.

**Rule:** when an extension point has lifetime constraints (e.g. "must be available
at construction"), check whether those constraints are intrinsic or accidental. Often
the "must be available" requirement is just coupling — defer the dependency to where
it's actually needed.

## L27 — Sentinel values (`Key="*"`) deserve schema-level documentation

Read audits for list endpoints use `Key="*"` as a sentinel. This is documented in
the spec, in code comments, and in retention SQL examples. Without that triple
coverage, a future query writer might filter `WHERE Key = X` and get confused about
the `*` row showing up. Sentinels are technical debt; their cost is partially repaid
by ruthless documentation.

**Rule:** when you introduce a sentinel value (magic string in a column that means
something special), document it in (a) the spec/contracts, (b) the writing code, and
(c) at least one retention/query example. Skipping any one of those triples the
chance of a future bug.

## L28 — UI bulk operations: don't add new bulk endpoints if a client loop suffices

B30 implemented Toggle IsSecret / Move to scope / Delete selected entirely client-side
via loops over existing PUT/DELETE endpoints. No new bulk endpoints needed. For an
editor-scale workload (humans selecting 5-50 entries), the chattiness is fine and the
implementation cost is trivial. Adding bulk endpoints would have meant a new contract
to design, document, and version — for no measurable benefit.

**Rule:** for any "bulk X" UI feature, first try a client-side loop. Add a server-side
bulk endpoint only when (a) the operation needs transactional all-or-nothing semantics,
(b) the typical batch size is 1000+, or (c) the per-item endpoint has meaningful
per-request overhead. None of those applied here.

---

# v0.7.0 lessons (2026-05-18)

## L29 — Demo-mode adapter pattern: lazy-loaded, runtime-gated, tree-shakeable

The Vite `--mode demo` flag AND a `?demo` runtime query string both activate the same
adapter in `client.ts`. The adapter is dynamically imported, so production bundles
don't pay for the demo data unless the user actively requests it via query string. The
runtime check sets up the API client once at module load — no per-call branching.

**Rule:** when shipping a "demo / mock" mode in a frontend library, use a runtime gate
+ dynamic import. The production bundle stays minimal; the demo bundle is reachable on
demand. NEVER use environment variables that bake the demo into the production bundle —
they leak demo data into prod and look amateurish.

## L30 — Screenshot determinism is non-trivial; document the known sources of drift

Playwright screenshots from `npm run screenshots` are 99.5% byte-identical run to run,
with a small ~0.5% drift from locale-formatted timestamps in the entries table
(`toLocaleString()` includes today's date). Other sources of drift:
- Font rendering differences across OSes (Windows ClearType vs Linux freetype)
- Chrome version updates (we pin Chromium via Playwright's bundled version)
- Animations (mitigated via `emulateMedia({reducedMotion: 'reduce'})`)

**Rule:** screenshot tests are an OK signal, not a hard regression gate. If the team
ever wants pixel-perfect: mock `Date.now()` in the demo adapter to a fixed UTC string,
disable locale-formatted dates in the UI for `?demo` mode, and pin OS + Chrome version
in CI. Most projects don't need this; document the known sources of drift instead.

## L31 — Docs and code drift slowly when they live in different places

Docs at `website/docs/*.md` need to stay aligned with `CLAUDE.md` + `.claude/rules/`
+ spec files. They duplicate content — same description in 3 places. The drift is
real and visible: v0.6.0's docs already overlap heavily with the spec sidecar.

**Rule:** treat docs as a derived artifact, not the source of truth. The source of
truth is the spec + the code + CLAUDE.md. Each release cycle's docs batch (we already
have one per version) regenerates the affected pages from the latest source. Skipping
the docs batch for a release means the docs lie about the current state — worse than
no docs at all.

---

# v0.8.0 lessons (2026-05-18)

## L32 — Semantic CSS variables beat per-component `dark:` prefixes

The v0.8.0 dark-mode migration was much smaller than expected because most components
already used semantic Tailwind tokens (`bg-background`, `text-foreground`, etc.) backed
by CSS variables. The `.dark` class flips the variable values; components are invariant.
Only a handful of explicit-color cases (Action chips with green/blue/red, AccessWarningBanner
amber) needed `dark:` variants.

**Rule:** when setting up a Tailwind project that will eventually support themes, use
semantic tokens (`bg-card`, `border-border`) from day one even when you only have one
theme. The migration cost is paid upfront; adding the second theme later is then a
config-file change, not a component sweep. Hardcoded color classes (`bg-white`,
`text-gray-900`) lock you into a theme.

## L33 — Tree views share selection state with flat views via composite keys

The flat-and-tree dual-view design needed selection to work across both. Solution: a
single `entriesStore.selectedKeys: Set<string>` keyed by composite `${scope}|${env}|${key}`,
read from both `EntriesTable` and `EntriesTreeView`. Switching views preserves selection;
bulk operations work uniformly. The tree view's local state is only the expansion set
(`expandedPrefixes: Set<string>`); selection is global.

**Rule:** when shipping multiple presentations of the same data with shared interactions,
the interaction state belongs in the store (one source of truth), not the presentation
component. The presentation owns only its display-specific state (expansion, scroll
position, etc.).

## L34 — Dialog `size` prop beats per-call max-width overrides

Before v0.8.0, each dialog passed `className="max-w-2xl"` or similar. v0.8.0 added a
`size: 'sm' | 'md' | 'lg' | 'xl'` prop on the base Dialog primitive with a width map.
Consumers say `size="xl"` and get a consistent ~72rem max-width; the primitive owns the
viewport guards (`max-w-[90vw] max-h-[90vh]`). Per-call className overrides are still
possible but never needed for size.

**Rule:** when N components consume a primitive with the same axis of variation (size,
intent, severity), expose a typed prop. Eliminates the "every consumer reinvents the
class string" pattern; lets the primitive enforce invariants (viewport guards) globally.

---

# v0.9.0 lessons (2026-05-18)

## L35 — Don't over-engineer multi-tenancy with custom IOptions wrappers

We initially shipped `ITenantAwareOptions<T>` with `GetForCurrentTenant()` and a separate
`ITenantContextAccessor` for the host to push tenant id into. This was wrong. The .NET
options pipeline already has the right shape: `IOptionsSnapshot<T>` rebinds per scope.
We just needed to make `IConfiguration[key]` reads tenant-aware.

The fix (B62): a single consumer-implemented `ITenantResolver` interface; `TryGet` consults it
on every read; standard `IOptionsSnapshot<T>` rides on top with zero ceremony. The custom
`ITenantAwareOptions<T>` abstraction, the `ITenantContextAccessor` accessor, the
`ITenantConfigReader` reader layer, the `TenantAwareOptionsBuilder` — all deleted.

**Rule:** when adding a "dynamic per-something" axis to an existing options pattern
(`IOptionsSnapshot<T>`, `IOptions<T>`), look at whether the underlying `IConfiguration`
can be made to vary per that axis. The options pipeline does the rest. Don't wrap IOptions
with a custom interface.

## L36 — Sync resolver in a sync `IConfiguration.TryGet` path

`ITenantResolver.Resolve()` returns `string?` synchronously because it's called from
`IConfiguration[key]` which is sync. Resolvers can read from `IHttpContextAccessor`
(sync claim/header access) cheaply. Resolvers MUST NOT do I/O (database lookup, HTTP call).
For complex tenant identification, the consumer pre-loads tenant state in middleware and
stores it somewhere the resolver can read cheaply (e.g. `IHttpContextAccessor.HttpContext.Items`,
a scoped service, or a custom `AsyncLocal<string?>`).

**Rule:** when a callback runs on a hot read path, keep it sync. If async is needed, the
caller's pipeline must change (`IOptionsSnapshotAsync<T>`? — not a real thing in .NET).
Document the constraint as a feature, not a limitation.

## L37 — `IOptions<T>` vs `IOptionsSnapshot<T>` matters for tenant-aware types

`IOptions<T>` is singleton-cached. The factory runs once at first access, typically at app
startup with no request scope. The resolver returns null. The cached T has GLOBAL values.
Every subsequent request reading `IOptions<T>` gets that global T forever, regardless of tenant.

`IOptionsSnapshot<T>` is scoped. The factory runs once per scope (per request). Resolver
returns the current tenant. The bound T reflects that tenant.

This isn't a db-config quirk — it's how .NET's options lifetimes work. Consumers must use
`IOptionsSnapshot<T>` for any tenant-aware type.

**Rule:** when shipping a config layer with per-request behavior, document the IOptions vs
IOptionsSnapshot distinction prominently. Consider a runtime warning when a tenant-aware
type is resolved via `IOptions<T>` (tracked for v0.10.0+).

## L38 — Tenant axis dominates the scope axis

When composing two orthogonal scoping dimensions (`IncludeScopes` and `TenantId`), one of
them has to win on equal-key conflicts. We picked tenant-dominates-scope: a tenant-specific
entry beats any global entry, regardless of which scope (own Scope vs IncludeScope) the
tenant entry lives in. Within a single tenant's bag (and within the global bag) the existing
Scope-beats-IncludeScopes rule applies recursively.

This makes the precedence walk a flat ordered list of four buckets (tenant×Scope,
tenant×IncludeScope, global×Scope, global×IncludeScope), not a 2-D matrix. Engineers can
mentally trace any lookup as "did the resolver give me a tenant? if yes, look there first;
else fall through to global." No tie-breaking between dimensions because one always wins.

The alternative (scope-dominates-tenant: own-scope global beats tenant override in a Shared
scope) would have been a worse default. Tenant overrides should be the user-facing override
mechanism; making them lose to a global in your own scope would mean tenant overrides
silently stop working as soon as you also seed a global default in your own Scope.

**Rule:** when composing orthogonal scoping dimensions, the more-specific dimension should
dominate uniformly. Per-tenant is more specific than global; let it win across all scopes,
not just within the scope where it was written. Document the rule prominently — it is the
shape of the API.

## L39 — Matrix tests for composition coverage

The four-bucket precedence walk has 2^4 = 16 presence-states (each bucket either holds a
value for the key or doesn't), times the resolver returning `null` vs a specific tenant id,
times multiple tenants. Hand-writing one test per state is tedious and forgets edge cases.

The pattern that worked: xUnit `[Theory]` with `[MemberData]` returning a data set that
encodes (bucket-seed-state, resolver-return-value, expected-result). One test method drives
the entire cube. Each row in the data set is a self-describing tuple; failures point at a
specific (seed, resolve, expected) triple so debugging is targeted.

For B64.1 we added 16+ rows covering: every combination of "Acme has override / doesn't",
"Globex has override / doesn't", "global×Scope seeded / not", "global×Shared seeded / not",
crossed with resolver returning Acme / Globex / null / empty-string.

**Rule:** when a feature's correctness is a truth table across N boolean axes, write one
parametric test driving the truth table from MemberData, not N hand-written tests. The
MemberData definition is the spec; future readers see the full state space at a glance.

## L40 — `IConfiguration.Bind` vs `TryGet` split is intentional

`DbConfigConfigurationProvider.TryGet` is tenant-aware, but the `Data` dictionary that
backs `GetChildKeys` / `AsEnumerable` only contains global (`TenantId = ""`) entries. This
asymmetry is deliberate defense-in-depth: tenant entries are reachable only via indexed
reads, not via enumeration that might walk the whole config tree.

The cost: `IConfiguration.Bind(section, options)` walks `GetChildKeys`, so if a tenant has
a key the global scope doesn't have, `Bind` won't populate it. Recommendation in the docs:
every tenant-overridable key should also exist in the global scope (even with a placeholder
value), so the global skeleton drives binding and tenant entries selectively override.

Overriding `GetChildKeys` to expose the current tenant's keys is tracked for v0.10.0+ — it
would require calling the resolver during enumeration, which has surprising perf implications
(every config-tree walk does a resolver call per key) and reintroduces the leak risk we
wanted defense-in-depth against.

**Rule:** when defense-in-depth dictates an asymmetric API (indexed reads see X; enumeration
doesn't), document the asymmetry as a feature, not a bug. List the practical consequences
(`Bind` misses tenant-only keys) so consumers know how to structure their data.
