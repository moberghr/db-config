---
sidebar_position: 5
---

# Multi-tenant config

DbConfig supports per-request multi-tenancy: one host serves N tenants, each with its own configuration overrides layered on top of a shared global default. Tenant resolution stays with the host — DbConfig ships the `ITenantResolver` interface and calls it on every `IConfiguration` read.

## Overview

When a request arrives, DbConfig calls `ITenantResolver.Resolve()` on every `IConfiguration[key]` read. If the resolver returns a non-empty tenant id, the tenant-specific entry is returned (with fallback to the global default if no tenant-specific entry exists). Standard `IOptionsSnapshot<T>` automatically becomes tenant-aware because it rebinds from `IConfiguration` once per request scope.

See [Resolution order](./resolution-order.md) for the canonical reference on how the four scoping dimensions (Environment, Scope + IncludeScopes, TenantId, Key) combine on every read.

```
Request (JWT with tenant_id = "Acme")
  → Handler → IOptionsSnapshot<StripeOptions>.Value
               → IConfiguration.Bind("Stripe", options)
               → DbConfigConfigurationProvider.TryGet("Stripe:ApiKey")
               → ITenantResolver.Resolve() → "Acme"
               → _tenantData["Acme"]["Stripe:ApiKey"] → Acme's key
               → StripeOptions with Acme's ApiKey
```

Global default entries have `TenantId = ""` (empty string). This is the sentinel for "no tenant" or "applies to all tenants not otherwise specified". Tenant-specific entries win over global defaults when both exist.

:::note
DbConfig does NOT ship built-in tenant resolution middleware — there are too many resolution strategies (JWT claim, header, route segment, subdomain) for a one-size-fits-all approach. The host implements `ITenantResolver` and registers it; DbConfig calls it.
:::

## Implementing a resolver

Implement `ITenantResolver` and return the current tenant id from whatever source fits your auth model:

```csharp
// JWT claim resolver — reads tenant_id from the authenticated user's claims
public class MyTenantResolver(IHttpContextAccessor http) : ITenantResolver
{
    public string? Resolve()
        => http.HttpContext?.User.FindFirst("tenant_id")?.Value;
}
```

Other examples:
- Header: `http.HttpContext?.Request.Headers["X-Tenant-Id"].ToString()`
- Route value: `http.HttpContext?.GetRouteValue("tenantSlug")?.ToString()`
- Subdomain: parse `http.HttpContext?.Request.Host.Host` to extract the first segment

`Resolve()` is called on every `IConfiguration[key]` read. **It must be cheap — no database calls, no HTTP calls, no blocking I/O.** If your tenant identification requires a database lookup, pre-load the tenant id in middleware and store it in a service that the resolver can read synchronously.

## Registering the resolver

Register the resolver inside the `AddDbConfig` options block:

```csharp
builder.Services.AddHttpContextAccessor();

builder.AddDbConfig(b =>
{
    b.Options.Scope = "PaymentService";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
    b.AddTenantResolver<MyTenantResolver>();
});
```

`AddTenantResolver<TResolver>()` registers `TResolver` as a singleton. The resolver is resolved from host DI and can take constructor dependencies (e.g. `IHttpContextAccessor`). If no resolver is registered, all reads return global config.

## Reading config

Use the standard `services.Configure<T>` registration. No custom options API is needed:

```csharp
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
```

Inject `IOptionsSnapshot<T>` in your services and handlers:

```csharp
public class PaymentService(IOptionsSnapshot<StripeOptions> opts)
{
    public void Charge()
    {
        var stripe = opts.Value; // current tenant automatically
        // opts.Value is Acme's StripeOptions if ITenantResolver.Resolve() returned "Acme"
    }
}
```

`IOptionsSnapshot<T>` is scoped per request. Its factory binds from `IConfiguration` once per scope, driving `TryGet` calls on the provider which consult the resolver. No additional setup needed.

## The IOptions&lt;T&gt; gotcha

:::warning
**`IOptions<T>` is singleton-cached. Do not use it for tenant-aware types.**

`IOptions<T>` caches the bound `T` instance at first access, which typically happens at app startup with no request context. At that point, `ITenantResolver.Resolve()` returns null. The cached `T` holds global values. Every subsequent request reading `IOptions<T>` gets that global `T` — regardless of tenant.

Use `IOptionsSnapshot<T>` (scoped per-request) for any type that needs tenant-specific values.
:::

```csharp
// WRONG — IOptions<T> is singleton; always gets global values
public class BrokenService(IOptions<StripeOptions> opts)
{
    public void Charge()
    {
        var stripe = opts.Value; // always global — tenant values never returned
    }
}

// CORRECT — IOptionsSnapshot<T> is scoped; gets tenant-specific values per request
public class CorrectService(IOptionsSnapshot<StripeOptions> opts)
{
    public void Charge()
    {
        var stripe = opts.Value; // tenant-specific (or global fallback)
    }
}
```

## Fallback semantics

When `ITenantResolver.Resolve()` returns `"Acme"` and the provider resolves a key:

1. Look up the Acme-specific entry `(TenantId = "Acme")`.
2. If found, return it.
3. If not found, fall back to the global default `(TenantId = "")`.
4. If neither exists, the key is absent (returns null / option property default).

This fallback means tenant-specific entries act as selective overrides. Entries without a tenant override transparently use the global value.

When `Resolve()` returns null or empty string, only global entries are consulted (no tenant lookup).

## Schema

The `DbConfig_Entries` and `DbConfig_AuditEntries` tables include a `TenantId` column:

```sql
DbConfig_Entries
  Scope       nvarchar(128)  NOT NULL
  Environment   nvarchar(128)  NOT NULL
  TenantId      nvarchar(128)  NOT NULL DEFAULT ''  -- "" = global default
  Key           nvarchar(512)  NOT NULL
  Value         nvarchar(max)
  IsSecret      bit            NOT NULL DEFAULT 0
  ModifiedUtc   datetimeoffset NOT NULL
  ModifiedBy    nvarchar(256)

  UNIQUE (Scope, Environment, TenantId, Key)       -- unique constraint
  INDEX  (Scope, Environment, TenantId, ModifiedUtc DESC)  -- watermark index
```

The empty string `""` is the global-default sentinel. It is stored literally in the column — not NULL. All four scope columns (`Scope`, `Environment`, `TenantId`, `Key`) use case-sensitive collation. Tenant identifiers are therefore case-sensitive: `"Acme"` and `"acme"` are distinct tenants.

## Editing tenant config via HTTP API

All existing endpoints accept a `?tenantId=` query string parameter (defaults to `""` — global default). The write endpoints accept `tenantId` in the request body.

| Method | Route | Tenant support |
|--------|-------|----------------|
| `GET` | `/{app}/{env}` | `?tenantId=Acme` — list that tenant's entries. `?allTenants=true` — list every tenant's entries (admin view). Omit → global defaults. |
| `GET` | `/{app}/{env}/{*key}` | `?tenantId=Acme` — single entry. `?fallback=true` — fall back to global default when tenant-specific is missing. |
| `PUT` | `/{app}/{env}/{*key}` | Body: `{ "value": "...", "isSecret": false, "tenantId": "Acme" }`. Omit `tenantId` → writes global default. |
| `DELETE` | `/{app}/{env}/{*key}` | `?tenantId=Acme` — delete tenant-specific entry. Omit → delete global default. |
| `GET` | `/{app}/{env}/audit/{*key}` | `?tenantId=Acme` — audit history for a specific tenant's entry. |

`ConfigEntry` JSON responses include a `tenantId` field. Global default entries have `tenantId: ""`.

## UI

The entries table has a **Tenant** column that shows each entry's tenant id (or a "Default" badge for global entries). The ScopeSelector has a **Tenant** input that lets admins view and edit any tenant's entries. The create/edit dialogs pre-fill the current tenant from the scope selector.

## Composing with `IncludeScopes`

Tenant resolution and [shared scopes](./scopes.md#including-shared-scopes) compose. A host can configure both at the same time:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.Scope = "PaymentService";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.IncludeScopes = ["Shared"];
    b.UseSqlServer(connectionString);
    b.AddTenantResolver<MyTenantResolver>();
});
```

The polling provider's load query becomes `WHERE Scope IN ('Shared', 'PaymentService') AND Environment = 'Production'` — every tenant's entries across both scopes come back in one round trip. The walk on read then applies two rules:

1. **Tenant axis dominates the scope axis.** A tenant-specific entry beats any global entry, regardless of which scope it lives in. An override in `Shared` for tenant `"Acme"` wins over a global entry in `PaymentService` when the resolver returns `"Acme"`.
2. **Within a single tenant's bag (and within the global bag), Scope beats IncludeScopes.** Same precedence rule as for global-only resolution: own Scope is read last during load and wins ties. Among multiple IncludeScopes, lowest-precedence-first in array order.

### Worked example

`PaymentService` with `IncludeScopes = ["Shared"]` and an `Acme` tenant override sitting in the Shared scope:

```
Scope          TenantId   Key                Value
-----------------------------------------------------------------
PaymentService   ""         Stripe:ApiKey      sk_live_global_payment
Shared           "Acme"     Stripe:ApiKey      sk_live_acme_shared
```

When the resolver returns `"Acme"`, `IConfiguration["Stripe:ApiKey"]` resolves to `sk_live_acme_shared` — the Shared-scope tenant override wins, even though `PaymentService` has a global default for the same key. Tenant dominates scope.

When the resolver returns `null`, the same read resolves to `sk_live_global_payment` — there is no global Shared row for this key, so the global PaymentService row applies.

See [Resolution order](./resolution-order.md) for the full precedence walk including all four buckets.

## Limitations

- **`IOptions<T>` not supported for tenant-aware types.** Use `IOptionsSnapshot<T>` (scoped per-request). See the gotcha section above.
- **Resolver must be sync and fast.** `Resolve()` is called on every `IConfiguration[key]` read. No I/O. Pre-cache complex tenant identification in middleware.
- **Memory scaling:** all tenants' entries are loaded into memory at startup and on each reload. The practical ceiling is approximately 10,000 tenants × 100 keys per tenant (~200 MB). Beyond this, lazy per-tenant loading is on the roadmap.
- **Tenants are case-sensitive:** `"Acme"` and `"acme"` are distinct tenant identifiers. Use consistent casing across all writes and reads.
- **Single `AddDbConfig` per host still applies** (§2.10). Multiple tenant-scoped `AddDbConfig` registrations on the same host are not supported.
