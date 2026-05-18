---
sidebar_position: 2
---

# Scopes

Every configuration entry is uniquely identified by the composite triple
`(AppName, Environment, Key)`. The `AppName` and `Environment` values are the scope of an
entry — they control which application and deployment tier owns it.

This page covers `AppName` scoping and the `IncludeScopes` composition. For per-request tenancy as a fourth dimension see [Multi-tenant config](./multi-tenant.md); for the full precedence walk across all four dimensions see [Resolution order](./resolution-order.md).

## Basic scoping

When you register DbConfig, you declare the scope your host reads:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.AppName = "PaymentService";
    b.Options.Environment = builder.Environment.EnvironmentName; // "Production"
    b.UseSqlServer(connectionString);
});
```

The polling provider fetches all entries where `AppName = "PaymentService"` and
`Environment = "Production"`. Entries for other apps are invisible to this host.

:::warning
Scope column comparisons are case-sensitive after the v0.5.0 `CaseSensitiveScopeColumns`
migration. `"MyApp"` and `"myapp"` are distinct scopes. Set `AppName` and
`IncludeScopes` with consistent casing across all your hosts.
:::

## Including shared scopes

Use `IncludeScopes` to pull configuration from one or more shared scopes in addition to
your own. A common pattern is a platform team owning a `Shared` scope and each app reading
from it:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.AppName = "PaymentService";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.IncludeScopes = ["PlatformDefaults", "Shared"];
    b.UseSqlServer(connectionString);
});
```

### Precedence ordering

`IncludeScopes` is ordered **lowest-precedence-first**. The `AppName` is always read
last and always wins ties. The effective precedence for the example above is:

```
PlatformDefaults < Shared < PaymentService
```

If the same key exists in multiple scopes, the own-scope value wins. If it exists in
`Shared` but not `PaymentService`, the `Shared` value is used. The standard pattern for
three tiers:

```csharp
b.Options.IncludeScopes = ["OrgGlobals", "PlatformDefaults", "Shared"];
// Effective: OrgGlobals < PlatformDefaults < Shared < AppName
```

### How the polling provider merges

The polling provider issues one SQL query covering all scopes:

```sql
SELECT * FROM DbConfig_Entries
WHERE AppName IN ('PlatformDefaults', 'Shared', 'PaymentService')
  AND Environment = 'Production'
ORDER BY ... -- in-memory re-sort to input list order
```

It then walks the result lowest-precedence-first and builds its key→value dictionary.
Later entries overwrite earlier ones. The watermark (`MAX(ModifiedUtc)`) covers all scopes
— a change in any included scope triggers a reload for every consumer that includes it.

## Worked example: PaymentService + Shared + PlatformDefaults

```
Scope               Key                            Value
-----------------------------------------------------------
PlatformDefaults    Logging:Level                  Warning
PlatformDefaults    Telemetry:SamplingRate         0.1
Shared              Logging:Level                  Information   ← overrides PlatformDefaults
Shared              Auth:Issuer                    https://auth.internal
PaymentService      Database:ConnectionString      Server=...    ← own scope, wins everything
PaymentService      Logging:Level                  Debug         ← overrides Shared
```

After merging, `PaymentService` in `Production` sees:
- `Logging:Level` → `Debug` (own scope wins)
- `Telemetry:SamplingRate` → `0.1` (from `PlatformDefaults`, not overridden)
- `Auth:Issuer` → `https://auth.internal` (from `Shared`)
- `Database:ConnectionString` → `Server=...` (own scope)

## Per-scope authorization with `scopeFilter`

When you mount `MapDbConfigHttp`, you can restrict writes to a specific `AppName` using the
`scopeFilter` parameter. Any request whose route `{appName}` does not match the filter
returns `403 Forbidden`.

This lets you carve the API surface by team:

```csharp
// App team — can only write to PaymentService scope
app.MapDbConfigHttp("/api/dbconfig", scopeFilter: "PaymentService")
   .RequireAuthorization("AppTeamAdmin");

// Platform team — can only write to Shared scope
app.MapDbConfigHttp("/api/dbconfig-shared", scopeFilter: "Shared")
   .RequireAuthorization("PlatformAdmin");
```

Both groups can have different route prefixes, different auth policies, and different
audiences. The `/reload` endpoint within each group fires the in-process reload signal and
is always allowed regardless of the scope filter.

:::note
The `scopeFilter` only restricts writes (and reads) via that group. The polling provider
still reads all configured `IncludeScopes` — `scopeFilter` does not affect what values the
host's `IConfiguration` resolves.
:::

## Naming conventions for shared scopes

Shared scope names are conventional, not reserved. Suggested names:

| Name | Typical owner |
|------|--------------|
| `Shared` | Organization-wide config consumed by all apps |
| `PlatformDefaults` | Platform team defaults (lower precedence than Shared) |
| `OrgGlobals` | Cross-tenant values at the top of the stack |

Avoid names that could collide with real application names: `Default`, `Common`, `Base`.

:::warning
Never put production secrets in a shared scope unless ALL applications that include it are
trusted to read those secrets. `IsSecret = true` controls at-rest encryption and UI
masking, but every application process that includes the scope can read every secret value.
:::

## Limitation: no parent/inheritance chain

The current `IncludeScopes` model is a flat list with explicit precedence. There is no
automatic parent-child inheritance (e.g. `Production` inheriting from `Default`). That
pattern (Option C from the v0.4.0 design) was deferred. If you need it, model it explicitly
in your `IncludeScopes` list.

## `?includeScopes=` on the HTTP list endpoint

The HTTP list endpoint supports ad-hoc multi-scope queries via query string:

```bash
GET /api/dbconfig/PaymentService/Production?includeScopes=Shared,PlatformDefaults
```

This returns the merged view with the same precedence rules. Entries include their source
`AppName`, so callers can see which scope each value came from.

## Scope column in the UI

Every row in the entries table shows a colored scope badge with the source `AppName`.
Rows from included scopes have edit and delete buttons disabled — the UI prevents accidental
writes to a scope that a different team owns. Use the scope-specific HTTP group (with
`scopeFilter`) or the platform team's admin UI to write to shared scopes.

See [Scopes in UI](../ui-editor/scopes-in-ui.md) for the UI selector and view modes.

## Tenant as a fourth scoping dimension

v0.9.0 added `TenantId` alongside `AppName`, `Environment`, and `Key`. Tenant composes with `IncludeScopes`: every entry (own-scope or included-scope) can have a tenant-specific override on top of its global default. Global default entries store `TenantId = ""`.

Tenancy is resolved differently from `IncludeScopes`. `IncludeScopes` is baked into the polling provider's load query at startup and stays fixed for the host's lifetime. Tenant id is picked on every `IConfiguration[key]` read by calling `ITenantResolver.Resolve()`. Consumers see the right tenant's values transparently via standard `IOptionsSnapshot<T>`.

The two dimensions compose with one rule: **tenant axis dominates the scope axis.** A tenant-specific entry beats any global entry regardless of which scope it lives in. Within a single tenant's effective values, the same scope precedence applies as for global (AppName beats IncludeScopes, in array order). See [Resolution order](./resolution-order.md) for the full precedence walk and worked examples; [Multi-tenant config](./multi-tenant.md) covers the resolver model, fallback, schema, and HTTP/UI surfaces.
