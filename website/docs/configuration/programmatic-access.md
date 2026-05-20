---
sidebar_position: 7
---

# Programmatic access to IConfigStore

Most application code reads configuration through `IOptionsSnapshot<T>` or `IConfiguration` — the standard ASP.NET Core pipeline already handles tenant fallback, hot reload, and decryption transparently. For the cases where you need to read explicitly (admin tools, background jobs that consult another tenant's settings, diagnostic endpoints, cross-tenant analytics), DbConfig v0.11.1 added six convenience overloads to `IConfigStore` so consumer code does not have to pass `appName` / `environment` on every call.

## When to use

| Use case | Use |
|---|---|
| Application code reading the current tenant's settings | `IOptionsSnapshot<T>` (already tenant-aware) |
| Quick lookup of a single value in a request handler | `IConfigStore.GetAsync(key, ct)` |
| Background job reading a specific tenant's settings | `IConfigStore.GetForTenantAsync<T>(tenantId, ct)` |
| Admin endpoint reading every entry across all apps | the explicit `(appName, env, ...)` overloads or `QueryAsync(...)` |

The new overloads do not replace the explicit-arg API — they layer on top. Custom non-EF `IConfigStore` implementations that do not maintain ambient state simply throw `NotSupportedException` from the convenience overloads; the explicit-arg methods keep working.

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
