# PaymentsApi — db-config feature showcase

> **NOT FOR PRODUCTION.** Built-in cookie login covers both the UI and the
> HTTP API under a single unified mount (`MapDbConfigAdmin`), single shared
> password from `appsettings.json`, mocked Stripe integration, ephemeral
> Data Protection key ring. This is a runnable demo, not a hardened service.

A multi-tenant SaaS that processes Stripe payments on behalf of merchants.
Each merchant (tenant) has their own Stripe account. Some config is global
(idempotency window, default currency), some is per-tenant (API key, webhook
secret, feature flags, payment limits). Operators rotate keys, flip feature
flags, and adjust limits live via the embedded db-config admin UI — no
redeploy needed.

## What this sample shows

- **`ITenantResolver`** reading the `X-Tenant-Id` header and resolving the
  current merchant on every `IConfiguration[key]` read.
- **`IOptionsSnapshot<T>`** binding per-tenant overrides with global fallback —
  one strongly-typed options class per concern (Stripe, FeatureFlags, Limits,
  Notifications).
- **`IsSecret` flag** encrypting Stripe API keys, webhook secrets, and Slack
  hooks at rest via the default ASP.NET Data Protection encryptor; plaintext
  for non-sensitive entries (currency, intervals, flags) for psql/SSMS
  debuggability.
- **Audit log** writing one row per Insert/Update/Delete in the SAME
  transaction as the mutation. Visible via the UI's per-row History button.
- **`MapDbConfigAdmin`** mounts both the admin UI and HTTP API under a single
  prefix (`/admin/dbconfig` and `/admin/dbconfig/api`). One built-in cookie
  login (`opts.UseBuiltInLogin<AppSettingsCredentialValidator>()`) gates both
  surfaces — the React app calls its own backend right after sign-in with no
  extra auth dance.
- **Live reload** through the polling provider (5s interval here) — change a
  value in the admin UI, see the next request resolve to the new value
  without a redeploy.
- **`IOptions<T>` vs `IOptionsSnapshot<T>` gotcha** demonstrated directly at
  `/api/diag/io` — `IOptions<T>` is singleton-cached, binds once at startup
  with no request scope, and therefore NEVER reflects per-tenant values.

## Run it (60-second start)

```bash
# Postgres
docker compose up -d

# App
dotnet run
```

Open `http://localhost:5000/admin/dbconfig` in a browser — the built-in
cookie login form (`/admin/dbconfig/login`) appears. Sign in with any
username and the password `demo-admin-key-12345` (value of `Auth:Password`).
**The UI loads all entries immediately after sign-in** (no Scope +
Environment input required as of v0.10.0). Use the toolbar filter fields
to narrow by Scope, Environment, or Tenant when needed. The HTTP API
lives at `/admin/dbconfig/api` and is covered by the same cookie. For
curl/Postman: either sign in via the browser first and reuse the cookie,
or wire your own auth scheme onto the route group with
`.RequireAuthorization(...)`. The store seeds itself with ~14 entries on
first boot — global defaults plus two tenants (`Acme`, `Globex`).

## Try these requests

### Global request (no tenant header)

```bash
curl -X POST http://localhost:5000/api/charges \
  -H "Content-Type: application/json" \
  -d '{"amount":1000,"customerId":"cus_001"}'
```

Returns `currency: "USD"`, `stripeApiKeyPrefix: ""` (no per-tenant API key
configured for the global default), and the global feature flags.

### Acme tenant — EUR default, NewCheckout enabled

```bash
curl -X POST http://localhost:5000/api/charges \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: Acme" \
  -d '{"amount":1000,"customerId":"cus_001"}'
```

Now `currency: "EUR"`, `stripeApiKeyPrefix: "sk_test_DEM..."`, and
`appliedFlags.newCheckout: true`.

### Globex — over the per-tenant limit (100 000)

```bash
curl -X POST http://localhost:5000/api/charges \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: Globex" \
  -d '{"amount":200000,"customerId":"cus_002"}'
```

Returns `422 Unprocessable Entity` with the error `amount_exceeds_max_charge`
and the resolved per-tenant `max: 100000`.

### Resolved-config transparency

```bash
curl http://localhost:5000/api/diag/config -H "X-Tenant-Id: Acme"
```

Returns every option value the calling tenant would resolve, with secrets
masked to the first 8 chars.

### The IOptions vs IOptionsSnapshot gotcha

```bash
curl http://localhost:5000/api/diag/io -H "X-Tenant-Id: Acme"
```

`ioptions_value.apiKeyPrefix` is empty (singleton bound once at startup with
no request scope); `ioptions_snapshot_value.apiKeyPrefix` is
`sk_test_DEM...` (rebinds per request with the tenant resolver). Always use
`IOptionsSnapshot<T>` for tenant-aware types.

### Resolver smoke test

```bash
curl http://localhost:5000/api/diag/who -H "X-Tenant-Id: Acme"
```

## Live reload demo

1. Open `http://localhost:5000/admin/dbconfig` (sign in via the built-in cookie form).
   The UI loads every entry — global defaults plus the `Acme` and `Globex` tenant rows.
2. Find the `Stripe:DefaultCurrency` entry in the `Acme` tenant row (sortable by tenant
   column, or filter via the toolbar with `Tenant: Acme`).
3. Edit it from `EUR` to `GBP` and save.
4. Re-run the Acme charge curl above within ~5s.
5. Response now shows `currency: "GBP"` — no redeploy, no process restart.

Behind the scenes:
- The HTTP PUT writes to `DbConfig_Entries` and an audit row to
  `DbConfig_AuditEntries` in the same transaction, then fires
  `IDbConfigReloadSignal.TriggerReload()`.
- The polling provider rebuilds its dictionary from
  `IConfigStore.GetAllForAllTenantsAsync` and fires `IChangeToken`.
- `IOptionsSnapshot<StripeOptions>` rebinds on the next request scope.

## What's wired

| Component | Where |
|---|---|
| `ITenantResolver` (X-Tenant-Id header) | `HeaderTenantResolver.cs` |
| `AddDbConfig` (single-call wireup) | `Program.cs` ~line 25 |
| `IOptionsSnapshot<T>` registrations | `Program.cs` ~line 35 |
| Unified admin auth (built-in cookie login) | `AppSettingsCredentialValidator.cs` |
| Mounted DbConfig admin (UI + API) | `Program.cs` `app.MapDbConfigAdmin(...)` |
| Demo data seed (idempotent) | `Program.cs` `SeedDemoDataAsync` |
| Business endpoints | `Program.cs` `/api/charges`, `/api/refunds`, `/webhooks/stripe`, `/api/diag/*` |
| Typed options | `Options/*.cs` |

## Limitations of this demo

- No real Stripe integration — `/api/charges`, `/api/refunds`, and
  `/webhooks/stripe` are mocked. Real code would use `Stripe-Signature`
  HMAC verification and a `StripeClient`.
- Business endpoints are UNAUTHENTICATED. Only the admin surfaces under
  `/admin/dbconfig` (UI + `/admin/dbconfig/api`) are cookie-gated. Wire your
  own auth on the business endpoints before doing anything real.
- The Data Protection key ring is ephemeral and process-scoped. Restarting
  the host invalidates all existing ciphertext. For multi-instance or
  restart-stable encryption, call
  `builder.Services.AddDataProtection().PersistKeysToFileSystem(...)`
  BEFORE `builder.AddDbConfig(...)`. See the top-level README.
- The seed runs only when the store is empty. To re-seed, truncate
  `DbConfig_Entries` and `DbConfig_AuditEntries` and restart.
- This sample uses Postgres only. For SQL Server, swap the project reference
  to `DbConfig.Provider.SqlServer` and replace `UsePostgreSql` /
  `UseNpgsql` with `UseSqlServer`.
