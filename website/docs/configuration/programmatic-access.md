---
sidebar_position: 7
---

# Programmatic access to configuration

Most application code reads configuration through `IOptionsSnapshot<T>` or `IConfiguration` — the standard ASP.NET Core pipeline already handles tenant fallback, hot reload, and decryption transparently. For the cases where you need to read explicitly (admin tools, background jobs that consult another tenant's settings, diagnostic endpoints, cross-tenant analytics), DbConfig provides two complementary services:

- **`ITenantConfigReader`** — typed bind that reuses the consumer's existing `services.Configure<T>(...)` registrations, scoped to an explicit tenant id. Recommended for application code that wants a fully-bound POCO without changing how sections are named.
- **`IConfigStore`** convenience overloads — direct DB reads that bypass `IConfiguration`. Returns raw `ConfigEntry` rows with metadata (`IsSecret`, `ModifiedUtc`, `ModifiedBy`) or POCO binds keyed off `typeof(T).Name` verbatim. Best for admin tooling and raw metadata access.

## When to use

| Use case | Use |
|---|---|
| Application code reading the current tenant's settings | `IOptionsSnapshot<T>` (already tenant-aware) |
| Application code reading a specific tenant's typed settings | `ITenantConfigReader.GetForTenant<T>(tenantId)` |
| Quick lookup of a single value in a request handler | `IConfigStore.GetAsync(key, ct)` |
| Admin tool reading raw entries with metadata (IsSecret, ModifiedUtc) | `IConfigStore.GetForTenantAsync(tenantId, key, ct)` |
| Background job reading every entry across all apps | the explicit `(appName, env, ...)` overloads or `QueryAsync(...)` |

The two services are complementary: `ITenantConfigReader` is high-level and respects all `services.Configure<T>(...)` registrations including `PostConfigure` and custom section paths. `IConfigStore` is low-level and bypasses `IConfiguration` entirely — useful when you need raw metadata or when no `IConfigureOptions<T>` is registered for the type you want to read.

Custom non-EF `IConfigStore` implementations that do not maintain ambient state simply throw `NotSupportedException` from the convenience overloads; the explicit-arg methods keep working. `ITenantConfigReader` is independent of the store implementation — it works against the polling provider directly.

## ITenantConfigReader: typed bind via the standard pipeline

```csharp
public class AdminController(ITenantConfigReader reader)
{
    public IActionResult CompareStripeAcrossTenants()
    {
        var acme = reader.GetForTenant<StripeSettings>("Acme");
        var globex = reader.GetForTenant<StripeSettings>("Globex");
        return Ok(new { acme, globex });
    }
}
```

The reader uses the section path the consumer **already registered**:

```csharp
// At startup — registered once.
services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));

// Anywhere later — uses the same "Stripe" section, different tenant.
var acme = reader.GetForTenant<StripeSettings>("Acme");
```

No reflection, no `typeof(T).Name` convention, no explicit section path argument. The reader sets an `AsyncLocal` tenant override on the polling provider for the duration of the call, then resolves `IOptionsSnapshot<T>` in a fresh DI scope. The standard `IOptionsFactory<T>` pipeline runs — `PostConfigure<T>`, code-based configurators, and custom section paths all behave identically to a normal request.

**Properties:**

- **Reuses existing registrations.** No second registration for tenant-explicit reads.
- **Other configuration sources merge naturally.** `appsettings.json`, environment variables, etc. are tenant-unaware and pass through unchanged.
- **No DB hit per call.** The polling provider has all tenants in memory; the reader is in-memory dictionary lookups.
- **Thread-safe.** AsyncLocal isolates concurrent calls on different async flows; the override never leaks to the host's ambient `IConfiguration` after the call returns.

**Returns the same shape as `IOptionsSnapshot<T>` would** for a request whose `ITenantResolver` returns the same tenant id. So if you have a binding sharp edge for `IOptionsSnapshot` (e.g. tenant-only keys without a global skeleton), the same sharp edge applies to the reader. This is by design — one binding pipeline, two access patterns.

## IConfigStore: raw entries + verbatim-type-name bind

### Section names: `typeof(T).Name` verbatim

The typed-bind overloads use the runtime type name as the section name, exactly.
`StripeOptions` → keys prefixed `StripeOptions:`. **Not** `Stripe:`.

This intentionally diverges from the standard ASP.NET Core convention
(`services.Configure<StripeOptions>(config.GetSection("Stripe"))` which uses
the short name). If you want the standard pattern via `IOptionsSnapshot<T>`,
keep using `Configure<T>`. The typed-bind overloads on `IConfigStore` are for
use cases where you read **other tenants'** values programmatically — they're
NOT meant to replace `IOptionsSnapshot<T>` for current-tenant reads.

Two viable patterns:

1. **Type name matches section name:** `class Stripe { ... }` bound from
   `Stripe:` — works with both
   `services.Configure<Stripe>(GetSection("Stripe"))` AND
   `store.GetForTenantAsync<Stripe>(tenantId)`.
2. **Parallel namespaces:** `Stripe:` keys consumed by
   `IOptionsSnapshot<StripeOptions>`, `StripeOptions:` keys consumed by
   `store.GetForTenantAsync<StripeOptions>(tenantId)` — what the demo
   currently shows.

Generic types: the CLR generic-arity suffix is stripped, so
`MyOptions<TKind>` binds from the `MyOptions:` prefix. Multiple
instantiations of the same open generic therefore share one section —
define a non-generic wrapper if you need separate sections per
instantiation.

If `ITenantResolver.Resolve()` returns `null`, empty, or whitespace, the
convenience overloads fall back to the global (`TenantId = ""`) layer. A
whitespace tenant id is never queried literally.

## The six convenience overloads

```csharp
// Single key — current tenant (via ITenantResolver) or global if no resolver.
Task<ConfigEntry?> GetAsync(string key, CancellationToken ct);

// All entries — current tenant or global.
Task<IReadOnlyList<ConfigEntry>> GetAllAsync(CancellationToken ct);

// Single key for an explicit tenant id.
Task<ConfigEntry?> GetForTenantAsync(string tenantId, string key, CancellationToken ct);

// All entries for an explicit tenant id.
Task<IReadOnlyList<ConfigEntry>> GetAllForTenantAsync(string tenantId, CancellationToken ct);

// Typed bind for the current tenant, merged on top of global defaults.
Task<T> GetAsync<T>(CancellationToken ct) where T : class, new();

// Typed bind for an explicit tenant id, merged on top of global defaults.
Task<T> GetForTenantAsync<T>(string tenantId, CancellationToken ct) where T : class, new();
```

`AppName` and `Environment` come from the `DbConfigOptions` registered when you called `builder.AddDbConfig(...)`. The current-tenant overloads consult the `ITenantResolver` you registered (or fall back to "global only" when no resolver is registered). The typed overloads merge tenant entries on top of global defaults (tenant wins on keys present in both layers).

## Example 1 — single-key lookup

```csharp
// In a request handler.
app.MapGet("/api/diag/log-level", async (IConfigStore store, CancellationToken ct) =>
{
    var entry = await store.GetAsync("Logging:Level", ct);
    return Results.Ok(new { value = entry?.Value });
});
```

`GetAsync(key, ct)` resolves the current tenant via the registered `ITenantResolver`. If the resolver returns a non-empty tenant id and a tenant-specific entry exists, that entry is returned. Otherwise the global (`TenantId = ""`) entry is returned. No fallback chain to other tenants.

## Example 2 — cross-tenant read in a background job

```csharp
public class NightlyReconcileService(IConfigStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Each tenant has its own Stripe key — we iterate explicitly because
        // there is no request scope to drive ITenantResolver.
        foreach (var tenantId in new[] { "Acme", "Globex", "Initech" })
        {
            var stripeKey = await store.GetForTenantAsync(tenantId, "Stripe:ApiKey", ct);
            // ... reconcile with stripeKey?.Value ...
        }
    }
}
```

`GetForTenantAsync(tenantId, key, ct)` returns ONLY the tenant-specific entry — there is no fallback to global. If you need the merged "tenant on top of global" semantics, use the typed overload below.

## Example 3 — typed POCO bind

```csharp
// Read the full StripeOptions for tenant "Acme" with global defaults filled in.
var stripe = await store.GetForTenantAsync<StripeOptions>("Acme", ct);
//   stripe.ApiKey          — from Acme (overrides global)
//   stripe.DefaultCurrency — from Acme (overrides global)
//   stripe.WebhookSecret   — from global (Acme did not override it)
```

The configuration section name is `typeof(T).Name` **verbatim** — no suffix stripping, no convention magic. For `StripeOptions`, the binder reads keys prefixed with `"StripeOptions:"`. For `FeatureFlagsOptions`, the prefix is `"FeatureFlagsOptions:"`.

This is intentional: the explicit naming avoids the trap where a refactor renames a type and silently breaks the binding. If you prefer a custom section name, use the explicit-arg overloads with `IConfiguration.Bind` directly.

The typed overloads merge tenant entries on top of global defaults:

- Global layer is read first (all entries with `TenantId = ""`).
- Tenant layer is overlaid (entries with the explicit `tenantId`).
- Keys present in both layers: tenant wins.
- Keys present only in global: global value passes through.

Secrets (`IsSecret = true`) are decrypted before the POCO is bound — you get plaintext on the returned instance.

## Cross-link

- [Resolution order](./resolution-order.md) — how the four scoping dimensions combine on every read.
- [Multi-tenant config](./multi-tenant.md) — `IOptionsSnapshot<T>` is the right tool for request-scoped reads; `IConfigStore` convenience overloads are for explicit / cross-tenant scenarios.
- [Audit log](./audit-log.md) — programmatic reads via `IConfigStore` do NOT generate audit rows. Only the HTTP GET endpoints write read audits when `DbConfigOptions.AuditReads = true`.
