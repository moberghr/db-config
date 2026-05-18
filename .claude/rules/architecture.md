# Architecture Patterns

## §2.1 — Single-Call Registration

`AddDbConfig` is one extension on `IHostApplicationBuilder` — works for
`WebApplicationBuilder` (ASP.NET Core) and `HostApplicationBuilder` (worker / generic
host). It does NOT call `BuildServiceProvider()`; it wires services, configuration
source, and reload signal in one shot.

```csharp
builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(30);
    b.Options.IncludeScopes = ["PlatformDefaults", "Shared"]; // optional; see §2.11
    b.UseSqlServer(connStr);   // or b.UsePostgreSql(connStr)
});
```

What this does internally:

1. Runs the user's lambda. `UseSqlServer`/`UsePostgreSql` capture (a) the EF Core
   `Action<DbContextOptionsBuilder>` and (b) an `IUniqueConstraintDetector` instance
   onto the `DbConfigBuilder`. Neither touches `builder.Services` directly.
2. Registers the HTTP-side stack into host DI: `DbConfigOptions`, `TimeProvider`,
   `IUniqueConstraintDetector`, `IDbContextFactory<DbConfigDbContext>`,
   `IConfigStore` (as `EfCoreConfigStore`), `DbConfigRegistrationMarker`,
   `IDbConfigReloadSignal` (factory that reads `marker.Source.Provider`).
3. Constructs the polling-side store DIRECTLY (no DI lookup): builds a private
   `DirectDbContextFactory<DbConfigDbContext>` from the same configure action, then
   `new EfCoreConfigStore(factory, detector, TimeProvider.System)`.
4. Creates `DbConfigConfigurationSource` backed by the polling store and adds it to
   `builder.Configuration`.

**Two stores, same DB.** The polling provider's store and the HTTP layer's store are
distinct `EfCoreConfigStore` instances. They share nothing in-process — the DB is the
source of truth, and the `IDbConfigReloadSignal` (fired by HTTP write endpoints)
coordinates cache invalidation in the polling provider. Custom non-EF stores (e.g.
Redis) currently cannot share a single instance across both sides; tracked for v0.4.0.

**Guards:** calling `AddDbConfig` twice on the same host throws (single-scope, §2.10).
The lambda MUST call a provider extension (`UseSqlServer` / `UsePostgreSql`) — if it
doesn't, `AddDbConfig` throws `InvalidOperationException` after the lambda runs.

## §2.2 — `IConfigStore` Abstraction

`IConfigStore` (in `DbConfig.Core`) is the only surface the polling provider touches.
Provider packages ship implementations; `InMemoryConfigStore` ships in `DbConfig.Core` for
tests.

```csharp
public interface IConfigStore
{
    Task<IReadOnlyList<ConfigEntry>> GetAllAsync(string appName, string environment, CancellationToken ct);
    Task<ConfigEntry?> GetAsync(string appName, string environment, string key, CancellationToken ct);
    Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string appName, string environment, CancellationToken ct);
    Task UpsertAsync(ConfigEntry entry, CancellationToken ct);
    Task DeleteAsync(string appName, string environment, string key, CancellationToken ct);
}
```

Rules:
- `GetAsync` MUST issue a single-row targeted query (`WHERE Key = @k`) — never call
  `GetAllAsync` and filter in memory for single-key HTTP endpoints.
- `GetLatestModifiedUtcAsync` is the only query the polling loop runs when nothing has
  changed. It MUST be cheap — uses the `(AppName, Environment, ModifiedUtc DESC)` index.
- `UpsertAsync` is last-writer-wins. Concurrent upserts to the same `(AppName, Env, Key)` must
  not throw; they resolve to one winner silently.
- `InMemoryConfigStore` uses a plain `Dictionary` and is NOT thread-safe in ways that matter
  for production; it is test-only by design.

## §2.3 — Polling Provider

`DbConfigConfigurationProvider` drives the polling loop:

1. On first `Load()`: build the deferred `IServiceProvider`, resolve `IConfigStore`, call
   `GetAllAsync`, populate the internal dictionary, start the `Timer`.
2. Each tick: call `GetLatestModifiedUtcAsync`. If the watermark advanced, call `GetAllAsync`
   and `OnReload()` (fires `IChangeToken`, triggers `IOptionsMonitor` callbacks downstream).
3. Failure during reload: log a warning; keep previous values; retry on next tick. Never throw
   from the timer callback.

`TimeProvider` is injected so tests can advance time without real wall-clock delays. Register
via `services.TryAddSingleton(TimeProvider.System)` in production (done automatically by
`Services.AddDbConfig`).

`IChangeToken` semantics: `OnReload()` calls `ChangeToken.OnChange(...)` callbacks registered
by `IOptionsMonitor<T>` and `IOptionsSnapshot<T>`. The provider itself does not hold any
reference to the consumer's callbacks — they are registered and released via the standard
`IDisposable` pattern.

## §2.4 — `IDbConfigReloadSignal`

`IDbConfigReloadSignal` lets the HTTP layer trigger an immediate reload without waiting for
the next polling tick.

```csharp
// POST /reload — fires the signal, returns 204
public static IResult Handle(IDbConfigReloadSignal signal)
{
    signal.TriggerReload();
    return Results.NoContent();
}
```

`PUT /{appName}/{env}/{*key}` and `DELETE /{appName}/{env}/{*key}` also call
`signal.TriggerReload()` after mutating the store, so in-process consumers see the updated
values immediately without waiting for the polling interval.

`IDbConfigReloadSignal` is resolved lazily from host DI. The concrete implementation
(`DbConfigConfigurationProvider`) is not available until after `WebApplication.Build()` runs
and the configuration source's `Load()` has been called. Resolving it during DI registration
time throws — resolve it inside a request handler or background service, never in
`ConfigureServices`.

## §2.5 — Package Boundaries

```
Moberg.DbConfig.Core
  └─ IConfigStore, ConfigEntry, DbConfigOptions, DbConfigBuilder
     DbConfigConfigurationSource, DbConfigConfigurationProvider
     InMemoryConfigStore (test helper)
     IDbConfigReloadSignal, DbConfigRegistrationMarker
     (no public DI extension lives here — see DbConfig.EntityFrameworkCore for AddDbConfig)

Moberg.DbConfig.EntityFrameworkCore   [TRANSITIVE PACKAGE — consumers don't install directly]
  └─ DbConfigDbContext, ConfigEntryEntity, EfCoreConfigStore
     IUniqueConstraintDetector
     HostApplicationBuilderExtensions (the single-call AddDbConfig entry point)
     ← references Core

Moberg.DbConfig.Provider.SqlServer
  └─ SqlServerUniqueConstraintDetector, UseSqlServer extension
     EF Core SQL Server provider registration, migrations assembly
     ← references EntityFrameworkCore (transitively Core)

Moberg.DbConfig.Provider.PostgreSql
  └─ PostgreSqlUniqueConstraintDetector, UseNpgsql extension
     EF Core Npgsql provider registration, migrations assembly
     ← references EntityFrameworkCore (transitively Core)

Moberg.DbConfig.Http
  └─ MapDbConfigHttp (returns RouteGroupBuilder), endpoint handlers
     IDbConfigAuthorizationFilter, IDbConfigCredentialValidator, DbConfigHttpOptions
     ← references Core

Moberg.DbConfig.Ui
  └─ MapDbConfigUi, MapDbConfigAdmin (unified UI + API mount)
     EmbeddedStaticFileMiddleware, built-in cookie login
     ← references Core + Http
```

**Why `EntityFrameworkCore` is its own package** (the v0.3.0 extraction): it ships ~500 lines
of EF Core plumbing (DbContext, EfCoreConfigStore, audit store, default encryptor, detector
abstraction, the AddDbConfig extension) that BOTH provider packages need. Without this
intermediate layer either (a) Core would have to carry the EF Core transitive dep onto
every consumer including those writing custom non-EF stores, OR (b) the same plumbing would
be duplicated across both provider packages with bug-fix drift risk. The csproj `<Title>`
and `<Description>` mark this package as `[TRANSITIVE — do not install directly]` so the
NuGet.org listing makes the role obvious.

Strict rule: **Core does NOT reference any other db-config package.** The dependency arrow
always points from provider/http/ui → EntityFrameworkCore → Core. Circular references are
build errors and must never be introduced.

## §2.6 — `IUniqueConstraintDetector` Strategy

`EfCoreConfigStore.UpsertAsync` catches `DbUpdateException` and delegates the "is this a
unique-constraint violation?" question to `IUniqueConstraintDetector`. The store treats a
detected violation as last-writer-wins (re-read, merge, re-save). The store does NOT contain
any provider-specific exception inspection code.

Each provider package ships its own detector:

```csharp
// SqlServer provider
public sealed class SqlServerUniqueConstraintDetector : IUniqueConstraintDetector
{
    public bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql &&
           (sql.Number == 2627 || sql.Number == 2601);
}

// PostgreSQL provider
public sealed class PostgreSqlUniqueConstraintDetector : IUniqueConstraintDetector
{
    public bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg &&
           pg.SqlState == "23505";
}
```

`UseSqlServer` / `UseNpgsql` register the detector as a singleton. `EfCoreConfigStore`
resolves it via constructor injection. This keeps all provider-specific knowledge inside the
provider packages.

## §2.7 — `DbConfigDbContext` and Migrations

`DbConfigDbContext` lives in `Moberg.DbConfig.EntityFrameworkCore` (shared across both
providers). It owns the `DbConfig_Entries` table model with its composite unique constraint on
`(AppName, Environment, Key)` and the polling index on `(AppName, Environment, ModifiedUtc DESC)`.

Migrations live in each provider package. Both providers configure:

```csharp
options.MigrationsAssembly("Moberg.DbConfig.Provider.SqlServer"); // or PostgreSql
```

This means `dotnet ef migrations add` must be run from the provider project, not from
`EntityFrameworkCore`. `Designer.cs` and `ModelSnapshot.cs` are maintained by hand for the
current small entity set.

Never put `DbConfigDbContext` back into `Core` — the package would pull
`Microsoft.EntityFrameworkCore.Relational` transitively onto all consumers, including those
who write custom non-EF stores.

## §2.8 — Authorization Composition

The packages ship NO `[Authorize]` attributes, NO hard-coded policies, NO authentication
middleware. The host owns identity and policy entirely. This mirrors CLAUDE.md §0.3.
Hosts have three composition shapes.

### Option A (common case, v0.10.0+) — Unified `MapDbConfigAdmin`

```csharp
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();
app.MapDbConfigAdmin("/admin/dbconfig", opts =>
{
    opts.UseBuiltInLogin<MyValidator>();
});
// → UI at  /admin/dbconfig
// → API at /admin/dbconfig/api
```

One call mounts both surfaces under one prefix. The same cookie filter that gates the UI
also gates the API; the cookie `Path` defaults to the unified prefix so the React app can
call its own backend (`/admin/dbconfig/api/*`) right after sign-in. Returns a
`DbConfigAdminEndpoints(Ui, Api)` record exposing both `RouteGroupBuilder`s for further
composition.

### Option B — Compose with the host's existing pipeline

```csharp
app.MapDbConfigHttp("/api/dbconfig").RequireAuthorization("DbConfigAdmin");
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig").RequireAuthorization("DbConfigAdmin");
```

The canonical pattern when the host already has an identity story (OIDC, Windows Auth,
JWT). Both endpoints surface as endpoint metadata so `RequireAuthorization` composes
exactly as it does on any other minimal-API route group. Use this shape when the UI and
HTTP API live at different prefixes (e.g. UI behind a CDN, API on a different subdomain).

### Option C — Built-in UI auth (split prefixes)

When the unified mount doesn't fit but you still want the built-in login form:

```csharp
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();

app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig", opts =>
{
    opts.UseBuiltInLogin<MyValidator>();
});

// Share the same cookie filter with the HTTP API at its own prefix:
app.MapDbConfigHttp("/api/dbconfig", http =>
{
    http.Authorization = uiOpts.Authorization;   // captured from the UI configure callback
});
```

Internally `UseBuiltInLogin` :
1. Auto-wires a `CookieAuthorizationFilter` (signed via `IDataProtectionProvider`,
   purpose `Moberg.DbConfig.Ui.Auth`).
2. Registers `GET /login`, `POST /login`, `POST /logout` inside the UI route group.
3. Attaches a `DbConfigUiAuthFilter` endpoint filter that redirects unauthorized
   browser requests to `/login?returnUrl=...` and returns 401 to non-browser callers.

Alternative shapes on the same `DbConfigUiOptions` surface:
- `opts.Authorization = new MyFilter()` — any `IDbConfigAuthorizationFilter` (e.g.
  the shipped `LocalRequestsOnlyAuthorizationFilter` for dev).
- `opts.UnauthorizedRedirectUrl = "/my-login"` — redirect browser requests to the
  consumer's own login page; combine with an authorization filter that inspects
  the consumer's auth state.
- `opts.CookiePath = "/"` — broaden cookie scope (e.g. when UI and API are siblings
  outside the same prefix). `MapDbConfigAdmin` sets this to its prefix automatically.

**Interface ownership:** `IDbConfigAuthorizationFilter` and `IDbConfigCredentialValidator`
live in `Moberg.DbConfig.Http` (not `Moberg.DbConfig.Ui`). They are auth contracts shared
by the unified mount, the UI built-in login, and any consumer that wants to attach a
filter directly to `MapDbConfigHttp` via the new
`MapDbConfigHttp(prefix, Action<DbConfigHttpOptions>)` overload without taking a UI
dependency.

## §2.9 — UI Embedding Pipeline

```
ui/ (React + Vite + TypeScript + Tailwind + shadcn)
  → npm run build
  → ui/dist/         (index.html + assets/)
  → CopyUIOutput MSBuild target copies dist/ into
    src/core/DbConfig.Ui/ as embedded resources
  → EmbeddedStaticFileMiddleware serves them at runtime
```

`index.html` contains a `<meta name="api-prefix" content="..." />` tag injected by
`EmbeddedStaticFileMiddleware` at serve time. The React app reads this tag on startup to know
which HTTP prefix to call. This avoids hard-coding the API URL in the built bundle.

The `BuildUI` MSBuild target runs `npm run build` during `dotnet build` when the
`ui/package.json` is present. Local development: run `npm run dev` in `ui/` — the dev server
proxies API calls to the host.

## §2.10 — Single-Scope Constraint

v0.4.0 supports exactly one `AddDbConfig` call per host. Calling it twice on the same
`IServiceCollection` throws immediately. This constraint exists because
`IDbConfigReloadSignal` resolution walks the registered marker to reach the first (and only)
provider instance.

Multi-scope support (multiple `(AppName, Environment)` pairs from different DB connections in
a single host) is not in scope for v0.4.0. Track it separately if required.

## §2.11 — Scope Merging and Precedence

When `DbConfigOptions.IncludeScopes` is non-empty, the polling provider reads from multiple
AppNames in one query and merges entries with explicit precedence.

**Scope ordering:** `[..IncludeScopes, AppName]` — own AppName always last (wins ties).

**Server contract:** `IConfigStore.GetAllScopedAsync(appNames, env, ct)` returns entries
ordered by their AppName's position in the input list. The polling provider iterates the
returned sequence and applies last-writer-wins to the internal dictionary. The server does
NOT merge — both the polling provider AND the UI receive the raw, ordered list and merge
client-side. This keeps the server side a thin pass-through and lets the UI render a
"shadowed by override" indicator.

**Watermark:** `GetLatestModifiedUtcScopedAsync(appNames, env, ct)` issues `MAX(ModifiedUtc)`
over the IN set. A change in any included scope advances the watermark.

**Authorization:** `MapDbConfigHttp(scopeFilter: "X")` enforces `path.appName == X` at the
group level. Use multiple groups with different filters + policies for app-team vs
platform-team auth separation. The `/reload` endpoint has no appName route value and is
always allowed (any group's reload affects only that host's in-process configuration).

## §2.12 — Encryption Strategy

Per-entry encryption tied to `IsSecret` flag. `IConfigEncryptor.Protect()` is called on
Upsert when `entry.IsSecret == true`; `Unprotect()` on read paths. Non-secret values
flow through verbatim.

Default impl `DataProtectionConfigEncryptor` wraps `IDataProtectionProvider` from
`Microsoft.AspNetCore.DataProtection`. Both polling-side and HTTP-side `EfCoreConfigStore`
instances share the SAME provider instance (constructed once in `HostApplicationBuilderExtensions.AddDbConfig`
and passed to the polling store directly; registered as `TryAddSingleton` for DI resolution
on the HTTP side).

**Custom encryptor:** register `services.AddSingleton<IConfigEncryptor>(myCustomImpl)` BEFORE
`builder.AddDbConfig`. The default `TryAddSingleton` registration is a no-op when a custom
impl is already present.

**Edge case:** flipping `IsSecret` post-hoc on a stored row will produce undefined behavior.
true→false leaves ciphertext sitting in a plaintext-shaped slot (the package skips decrypt
when IsSecret=false). false→true on a plaintext value will throw on `Unprotect`. This is
defensible — auditable tampering is preferable to silent corruption.

Audit rows store OldValue/NewValue in the same encrypted form as the main store's Value
column. The audit reader's `GetHistoryAsync` decrypts via the shared encryptor.

### Type-mapped registrations and deferred decryption (v0.6.0+)

Two encryptor registration shapes are supported:

1. **Instance-registered:** `services.AddSingleton<IConfigEncryptor>(myInstance)` — the
   instance is shared synchronously between the polling-side store and the HTTP-side
   store via `AddDbConfig`'s inspection of `ImplementationInstance`. Encryption works
   immediately on first `Load()`.
2. **Type-mapped or factory-registered:** `services.AddSingleton<IConfigEncryptor, MyImpl>()`
   — DI resolves the encryptor (and its dependencies) after the host is built. A
   `DbConfigEncryptorActivator` hosted service runs `StartAsync` to resolve the
   encryptor and call `provider.SetEncryptor(...)`. Until then, the polling provider
   stores raw ciphertext in its dictionary.

**Pre-build secret reads:** with type-mapped registration, reading a secret config value
before `host.StartAsync` (which fires the hosted services) throws
`InvalidOperationException` with a clear message. Non-secret reads are unaffected.
This is intentional — pre-build secret reads are rare (most code reads config in
request handlers) and a clear error is preferable to silently returning ciphertext.

**Provider tracking:** the polling provider maintains `Dictionary<string, bool>
_isSecretByKey` populated during Load() so it knows which keys require decryption.
The override of `TryGet` consults this dictionary and the nullable `_encryptor` field
to decide whether to decrypt, return as-is, or throw.

## §2.13 — Audit Log Integration

`DbConfig_AuditEntries` is a sibling table to `DbConfig_Entries`. Schema mirrors the spec.
No foreign key (entries may be deleted; audit rows must survive).

**In-transaction writes:** `EfCoreConfigStore.UpsertAsync` and `DeleteAsync` capture the
old row state, perform the mutation, then add a `ConfigAuditEntryEntity` to the same
`DbContext` tracker, then call `SaveChangesAsync` once. Both rows commit atomically;
either both succeed or both roll back.

**Action enum:** stored as a `nvarchar(16)` string column for migration friendliness. Values:
`Insert`, `Update`, `Delete`.

**Encryption:** OldValue and NewValue carry the same form as the source entry's Value column
(ciphertext when IsSecret=true). The audit reader decrypts before returning to the caller.

**Read API:** `IConfigAuditStore.GetHistoryAsync(appName, env, key, take, ct)` issues a
targeted query on the composite index `(AppName, Environment, Key, ModifiedUtc DESC)` and
returns up to `take` rows ordered most-recent-first. The HTTP endpoint
`GET /{app}/{env}/audit/{*key}?take=N` caps `take` at 500.

**Disable per-host:** `DbConfigOptions.EnableAuditLog = false` skips all audit writes
(no perf cost). The audit table can stay empty or be excluded from migrations entirely
(consumer choice; default is migration applies and audit writes happen).

**Audit action semantics:** the `Action` field reflects the database state transition at the
moment of `SaveChangesAsync`. Under concurrent inserts, the losing writer's audit row is
`Update` (not `Insert`) — by the time it commits, the row exists and the operation is
semantically an update. This is the correct behavior; do not "fix" it to reflect caller intent.

**Retention:** out of scope for the package. Document manual cleanup or an opt-in
`UseDbConfigAuditPruning(TimeSpan)` extension (deferred to v0.6.0).

## §2.14 — Read Auditing

When `DbConfigOptions.AuditReads = true`, HTTP GET endpoints write **fire-and-forget**
audit rows with `Action=Read`. Old/New values are null — the read itself isn't a state
change; the audit captures only WHO accessed WHAT WHEN.

**Why fire-and-forget instead of in-transaction:** read auditing should not double the
read latency. Every GET would otherwise acquire a DB write transaction. The trade is
"slight chance of missing audit rows on process crash" vs "GET latency stays small".
For compliance posture this is documented; consumers needing zero-loss audit reads
must layer their own middleware in front of `MapDbConfigHttp`.

**Recursion guard:** the audit history endpoint (`GET /{app}/{env}/audit/{*key}`) is
explicitly excluded from read auditing. Reading the audit log doesn't itself generate
audit entries.

**Key sentinel:** read audits for `GET /{app}/{env}` (list) use `Key="*"` to indicate
"the entire scope was listed". This is a sentinel value — no real config key may use
literal `*` because of route normalization in the catch-all endpoints.

**Failure mode:** if `IConfigAuditStore.WriteAsync` throws, the failure is caught and
logged at Warning level via `ILogger<TEndpoint>`. The GET response is unaffected. This
is acceptable because the alternative — failing the GET due to a back-end audit
infrastructure problem — would mean a single misconfigured audit store could brick
the entire read path.

## §2.15 — Multi-Tenant Architecture

v0.9.0 adds tenant-aware reads to the existing `IConfigStore` and `IConfiguration` pipeline. The design is deliberately thin: one consumer-implemented interface, no custom options API.

### The single abstraction: `ITenantResolver`

```
ITenantResolver.Resolve()
  ↓ (consumer-implemented; called on every IConfiguration[key] read)
DbConfigConfigurationProvider.TryGet
  ↓ (if resolver returns non-empty tenantId: check _tenantData[tenantId][key])
  ↓ (if not found or tenantId is empty: check global Data[key])
IConfiguration / IOptionsSnapshot<T>
  ↓ (standard ASP.NET Core pipeline; no custom options type needed)
Application code
```

**`ITenantResolver`** is a consumer-implemented interface with one method: `string? Resolve()`. The consumer reads tenant identity from whatever source fits their auth model — JWT claim, request header, route value, subdomain. db-config does not ship a resolver. Registered via `b.AddTenantResolver<TResolver>()` inside `AddDbConfig`; resolved as a singleton from host DI. If no resolver is registered, `NullTenantResolver.Instance` (returns null — global-only) is used.

**`DbConfigConfigurationProvider.TryGet`** is tenant-aware. On every `IConfiguration[key]` read it:
1. Resolves `ITenantResolver` from host DI (cached after first call post-build).
2. Calls `Resolve()`. If null or empty, skips to step 4.
3. Looks up `_tenantData[tenantId][key]`. If found, returns it (decrypted if secret).
4. Falls back to the global (`TenantId = ""`) entry in the base `Data` dictionary.
5. Returns false if neither exists.

**`IOptionsSnapshot<T>`** is scoped per request. Its factory calls `IConfiguration.Bind(section, options)` once per scope, which drives `TryGet` calls. Because the resolver returns the current tenant at bind time, `IOptionsSnapshot<T>.Value` automatically reflects the current request's tenant. No custom options interface needed.

### Memory model

All tenants' entries are loaded eagerly at startup and on each reload via `GetAllForAllTenantsAsync`. The provider holds two `ConcurrentDictionary<string, Dictionary<string, string?>>` structures:
- `_tenantData` — per-tenant entries: `tenantId → (key → rawValue)`
- `_isSecretByTenantKey` — per-tenant secret flags: `tenantId → (key → isSecret)`

The base `Data` dictionary (used by the standard `ConfigurationProvider.TryGet` path) holds ONLY global (`TenantId = ""`) entries. This is a read-path shortcut — global-only consumers that registered no resolver hit the fast path without touching `_tenantData`.

Memory ceiling: ~10K tenants × 100 keys (~200 MB). Lazy per-tenant loading is tracked for v0.10.0+.

### Defense-in-depth note

The OLD design (B52–B61) kept tenant data completely out of `IConfiguration` — the defense was "a bug in TenantAwareOptions cannot leak across tenants because IConfiguration never sees tenant entries."

The NEW design (B62+) exposes tenant data through `IConfiguration[key]` reads — `TryGet` selects the tenant-specific entry. The defense is now: **`ITenantResolver` IS the tenant context**. There is no cross-tenant leakage unless the resolver returns the wrong tenant id. The resolver is consumer-written, consumer-tested, and consumer-audited. A buggy resolver is the primary security risk; document this explicitly.

### IOptions&lt;T&gt; caveat

`IOptions<T>` is singleton-cached. Its factory runs once at first access, typically at app startup when no request scope exists. The resolver returns null. The bound `T` has global values and never changes. Consumers MUST use `IOptionsSnapshot<T>` for tenant-aware types. See also CLAUDE.md §0.8.

## §2.16 — Resolution Order

Every `IConfiguration[key]` read walks four scoping dimensions. Two are decided at host startup and frozen; two are decided per read.

| Dimension | DB filter | Composable? | Decided at |
|---|---|---|---|
| Environment | hard scalar `WHERE Environment = @env` | No | Startup (`DbConfigOptions.Environment`) |
| AppName | hard `WHERE AppName IN (own + IncludeScopes)` | Yes via `IncludeScopes` | Startup (`DbConfigOptions.AppName` + `IncludeScopes`) |
| TenantId | not in DB query — all tenants loaded into memory | Per-read | `ITenantResolver.Resolve()` on every `TryGet` |
| Key | dictionary lookup | n/a | The `IConfiguration[key]` argument |

**Load-time vs read-time split.** The polling provider issues ONE query per reload tick that fetches every row for this host's `(AppName ∪ IncludeScopes, Environment)` slice, across ALL tenants. The result is split into `Data` (global, `TenantId = ""`) and `_tenantData[tenantId][key]`. Reads then do pure in-memory dictionary lookups.

**Precedence walk** (pseudocode, called from `DbConfigConfigurationProvider.TryGet`):

```
tenantId = resolver?.Resolve()
if !string.IsNullOrEmpty(tenantId):
    if _tenantData[tenantId][key] from row where AppName == Options.AppName → return        # 1
    if _tenantData[tenantId][key] from row where AppName ∈ IncludeScopes    → return        # 2
if Data[key] from row where AppName == Options.AppName                      → return        # 3
if Data[key] from row where AppName ∈ IncludeScopes                         → return        # 4
return not-found
```

Steps 2 and 4 resolve in-memory at load time: the load walks scopes lowest-precedence-first, and later writes to the same key in the same tenant bag overwrite earlier ones. So by the time `TryGet` runs, the bag already holds the winning value for that bucket.

**Composition rule.** The tenant axis dominates the scope axis. A tenant-specific entry beats any global entry, regardless of whether the tenant entry came from the own AppName or an IncludeScope. Within a single bag, AppName beats IncludeScopes (matches §2.11).

**Sharp edges** (terse — long-form at `website/docs/configuration/resolution-order.md`):

- `IConfiguration.AsEnumerable()` / `GetChildren()` walk `Data` and see global entries only. Defense-in-depth.
- `IConfiguration.Bind` uses `GetChildKeys`, which today returns global keys only. Tenant-only keys are invisible to `Bind` unless a global skeleton exists. Recommend a global placeholder for every tenant-overridable key.
- Resolver exceptions propagate out of `TryGet`. Resolvers must be exception-safe.
- Tenant ids are case-sensitive (project-specific.md §8.14).
- Empty string from `Resolve()` is treated as null (no tenant context, global-only).
- `IOptions<T>` is singleton-cached and never sees a tenant; use `IOptionsSnapshot<T>` for any tenant-aware type (§2.15, CLAUDE.md §0.8).
- `IsSecret` post-hoc flag flip is undefined behavior (§2.12).
