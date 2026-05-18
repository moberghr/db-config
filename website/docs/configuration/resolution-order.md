---
sidebar_position: 6
---

# Resolution order

When you call `IConfiguration["Stripe:ApiKey"]` (or read `IOptionsSnapshot<StripeOptions>.Value`), DbConfig walks four scoping dimensions to pick the right value. This page is the canonical reference for how that walk happens.

For per-dimension detail see [Scopes](./scopes.md) and [Multi-tenant config](./multi-tenant.md). This page focuses on how the four dimensions compose at read time.

## The four dimensions

| Dimension | DB filter | Composable? | Where decided |
|---|---|---|---|
| Environment | hard scalar `WHERE Environment = @env` | No | At startup via `DbConfigOptions.Environment`; never changes |
| AppName | hard `WHERE AppName IN (own + IncludeScopes)` | Yes, via `IncludeScopes` | At startup via `DbConfigOptions.AppName` + `IncludeScopes` |
| TenantId | not in DB query — every tenant loaded into memory | Picked per-read | Via `ITenantResolver.Resolve()` on every `TryGet` |
| Key | dictionary lookup in memory | n/a | Via the `IConfiguration[key]` argument |

`Environment` and `AppName` are decided at host startup and frozen for the lifetime of the process. `TenantId` is decided on every read, and `Key` is the lookup argument itself.

## Load time vs read time

DbConfig splits work between a periodic load and a per-read resolve:

```
[ LOAD (every ReloadInterval, and on /reload signal) ]
  SELECT * FROM DbConfig_Entries
   WHERE AppName IN (own AppName + IncludeScopes)
     AND Environment = @env
  → builds three in-memory structures:
      Data                    — global entries only (TenantId = "")
      _tenantData             — tenantId → (key → rawValue)
      _isSecretByTenantKey    — tenantId → (key → isSecret)

[ READ (every IConfiguration[key] call) ]
  TryGet(key)
   → resolver.Resolve() → tenantId
   → walk precedence (tenant-specific first, then global; AppName beats IncludeScopes)
   → decrypt if IsSecret
   → return value
```

The DB query happens once per reload tick — it covers every tenant for this host's `(AppName ∪ IncludeScopes, Environment)` slice. The walk happens on every read, but it is pure dictionary lookups against the in-memory snapshot.

## Precedence walk for a single read

For a key, `TryGet` walks four candidate buckets in this order and returns the first match:

1. **Tenant-specific, own AppName** — `_tenantData[tenantId][key]` where the row's `AppName` equals `Options.AppName`.
2. **Tenant-specific, IncludeScope** — same `_tenantData[tenantId][key]`, but the row's `AppName` is one of `IncludeScopes`. Among multiple IncludeScopes, lowest-precedence-first in array order; later entries overwrite earlier ones during load.
3. **Global, own AppName** — `Data[key]` from a row with `TenantId = ""` and `AppName = Options.AppName`.
4. **Global, IncludeScope** — `Data[key]` from a row with `TenantId = ""` and `AppName` in `IncludeScopes`, lowest-precedence-first.

Two rules emerge:

- **Tenant axis dominates the scope axis.** A tenant-specific entry beats any global entry, regardless of which scope (own AppName vs IncludeScope) it lives in.
- **Within a single bag (tenant-specific or global), scope precedence is AppName-wins.** This matches the rule from [Scopes](./scopes.md): own AppName is read last during load, so it wins ties.

If the resolver returns `null` or `""`, steps 1 and 2 are skipped — the read becomes global-only.

## Worked example

Configuration:

```csharp
b.Options.AppName = "PaymentService";
b.Options.Environment = "Production";
b.Options.IncludeScopes = ["PlatformDefaults", "Shared"];
b.AddTenantResolver<MyTenantResolver>();
```

Seeded rows:

```
AppName          TenantId   Key                Value
-----------------------------------------------------------------
PaymentService   ""         Stripe:ApiKey      sk_live_global_payment
PaymentService   "Acme"     Stripe:ApiKey      sk_live_acme_payment
Shared           ""         Stripe:ApiKey      sk_live_global_shared
Shared           "Acme"     Stripe:ApiKey      sk_live_acme_shared
Shared           "Globex"   Logging:Level      Debug
```

Resolution table for `IConfiguration["Stripe:ApiKey"]`:

| Resolver returns | Walked buckets | Result |
|---|---|---|
| `"Acme"` | Tenant×AppName hits | `sk_live_acme_payment` |
| `"Globex"` | Tenant buckets miss; global×AppName hits | `sk_live_global_payment` |
| `null` | Tenant buckets skipped; global×AppName hits | `sk_live_global_payment` |

Resolution table for `IConfiguration["Logging:Level"]`:

| Resolver returns | Walked buckets | Result |
|---|---|---|
| `"Globex"` | Tenant×AppName miss; Tenant×Shared hits | `Debug` |
| `"Acme"` | Tenant buckets miss; global buckets miss | `null` |
| `null` | Tenant buckets skipped; global buckets miss | `null` |

Note the second row of the second table: Acme has overrides for `Stripe:ApiKey` but not `Logging:Level`. Tenant fallback drops to the global bag — which for `Logging:Level` is empty here, so the read returns `null`. There is no further fallback to Globex's tenant data (tenants do not borrow from each other).

## Why Environment is special

`Environment` is the only hard scalar filter. One host process serves exactly one environment. The load query bakes `WHERE Environment = @env` into every read, and the in-memory snapshot has no notion of cross-environment entries.

There is no environment inheritance (no "Production inherits from Default"). If you need cross-environment patterns, model them at the HTTP/UI layer — for example, a tool that copies a key set from `Staging` to `Production` through `PUT` calls — not at the runtime layer.

## Sharp edges

### `IConfiguration.AsEnumerable()` and `GetChildren()` see global only

The base `ConfigurationProvider.Data` dictionary holds only `TenantId = ""` entries. Enumeration APIs that walk `Data` (e.g. `AsEnumerable()`, `GetChildren()`, the debug view) therefore see global entries only. This is intentional defense-in-depth — it makes it harder for code that loops over `IConfiguration` to leak across tenants. Tenant entries are reachable only through indexed `[key]` reads or `Bind`.

### `IConfiguration.Bind` misses tenant-only keys

`Bind` walks the configuration tree via `GetChildKeys`, which today returns global keys only. If a key exists in a tenant's bag but not in the global bag, `Bind` will not populate it on the target object; a direct `IConfiguration["X:Y"]` read will still see it.

**Recommendation:** every tenant-overridable key should exist in the global scope, even as a placeholder or default. Tenant entries then act as selective overrides on top of the global skeleton.

### Resolver exceptions propagate

If `ITenantResolver.Resolve()` throws, the exception propagates out of `IConfiguration[key]` and `IOptionsSnapshot<T>.Value`. The configuration system does not catch it. Resolver implementations must be exception-safe — return `null` for "no tenant" rather than throwing on a missing claim.

### Tenant ids are case-sensitive

After the v0.5.0 collation fix, all four scope columns use case-sensitive comparison. `"Acme"` and `"acme"` are distinct tenants in the DB. The resolver is responsible for normalizing casing — DbConfig does not normalize. Use consistent casing across all writes and reads.

### `IOptions<T>` vs `IOptionsSnapshot<T>`

`IOptions<T>` is singleton-cached. Its factory runs once at first access (typically at startup with no request scope), the resolver returns null, and the bound `T` holds global values forever. Consumers MUST use `IOptionsSnapshot<T>` (scoped per-request) for any tenant-aware type. See the [IOptions gotcha](./multi-tenant.md#the-ioptionst-gotcha) on the multi-tenant page.

### `IsSecret` post-hoc flag flip is undefined behavior

Flipping `IsSecret` on a stored row after the fact produces undefined behavior. `true → false` leaves ciphertext in a plaintext-shaped slot (the decrypt step is skipped). `false → true` causes the next read to attempt to decrypt a plaintext value and throw. Delete and re-insert if you need to change the flag.

## See also

- [Scopes](./scopes.md) — `AppName` and `IncludeScopes` in detail
- [Multi-tenant config](./multi-tenant.md) — `ITenantResolver`, fallback, schema
- [Encryption](./encryption.md) — how `IsSecret` interacts with reads
- Architecture rule §2.16 — engineering reference in `.claude/rules/architecture.md`
