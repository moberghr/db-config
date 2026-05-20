---
sidebar_position: 3
---

# Demo host

The `samples/PaymentsApi` project is a minimal ASP.NET Core 8 application that wires up
the full DbConfig stack — PostgreSQL provider, unified admin mount, embedded UI, built-in
cookie login, multi-tenant resolver, and auto-migrating schema. Use it to explore the
features locally before integrating DbConfig into your own application.

## Clone and run

### Prerequisites

- .NET 8 SDK
- Docker (for the bundled PostgreSQL container) or any reachable PostgreSQL instance
- Node.js is **not** required — the React UI is pre-built into the NuGet package

### 1. Clone the repo

```bash
git clone https://github.com/moberghr/db-config.git
cd db-config
```

### 2. Start PostgreSQL

The sample ships a `docker-compose.yml` with PostgreSQL on `localhost:5432`:

```bash
cd samples/PaymentsApi
docker compose up -d
```

### 3. Run the sample

```bash
dotnet run
```

The sample applies DbConfig migrations automatically on startup (`SchemaMode.CreateIfMissing`
is the default), so you do not need to run `dotnet ef database update` manually.
On first run it also seeds a few dozen demo entries across two apps (`PaymentsApi` and
`Notifications`) and three tenants (Global / Acme / Globex), plus a small varied audit
history so the Audit Log page has something interesting to show.

Open `http://localhost:5000/` to see the landing message. Navigate to
`http://localhost:5000/admin/dbconfig` and sign in with any username and the value of
`Auth:Password` from `appsettings.json` (the demo default is `demo-admin-key-12345`).

### 4. Hit a tenant-aware business endpoint

```bash
# As tenant Acme
curl -X POST http://localhost:5000/api/charges \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: Acme" \
  -d '{"amount": 1000, "customerId": "cust_1"}'

# As global default (no tenant header)
curl -X POST http://localhost:5000/api/charges \
  -H "Content-Type: application/json" \
  -d '{"amount": 1000, "customerId": "cust_1"}'
```

The response includes the resolved Stripe key prefix, currency, feature flags, and limits.
Switching the `X-Tenant-Id` header switches the entire bundle of resolved options — the
`HeaderTenantResolver` reads the header on every request and `IOptionsSnapshot<T>` rebinds
per scope.

## Authentication: built-in cookie login (NOT for production)

The sample wires the admin surface with db-config's built-in cookie login. One call
mounts UI + HTTP API under one prefix and gates both with one signed cookie:

```csharp
builder.Services.AddScoped<IDbConfigCredentialValidator, AppSettingsCredentialValidator>();

app.MapDbConfigAdmin("/admin/dbconfig", opts =>
    opts.UseBuiltInLogin<AppSettingsCredentialValidator>());
```

`AppSettingsCredentialValidator` checks the submitted password against `Auth:Password` from
`appsettings.json`. The validator returns a `ClaimsPrincipal` on success; the package
signs and issues a cookie via `IDataProtectionProvider`. Cookie path is scoped to
`/admin/dbconfig` so the React app's HTTP calls to `/admin/dbconfig/api/*` see it
automatically.

:::warning
The `AppSettingsCredentialValidator` accepts any username with a single shared password.
It exists to demonstrate the composition pattern, not to be copied into production.
Production hosts implement `IDbConfigCredentialValidator` against their actual user store
(EF Core, ASP.NET Identity, LDAP) and hash-verify with PBKDF2 / Argon2 / bcrypt.
:::

See [Authentication & authorization](../configuration/auth.md) for the four auth patterns
the package supports (open access, host pipeline composition, built-in cookie, custom
authorization filter).

## What to explore in the demo

| Feature | Where to find it |
|---------|-----------------|
| Flat entries list across all apps + tenants | `/admin/dbconfig` (loads everything on first paint) |
| Audit Log tab with Insert / Update / Delete / Read chips | `/admin/dbconfig` → Audit Log |
| Tenant selector | Top-right of the UI; type `Acme` or `Globex` |
| Tree view with `Notifications:Email:Smtp:*` hierarchy | List mode toggle (table / tree) |
| Per-row history dialog with diff | Clock icon on any row |
| Wide Edit dialog (xl, 1152px) | Click anywhere on a row (except checkbox / eye / action buttons) |
| Secret reveal toggle | Eye icon next to any `IsSecret = true` row |
| Bulk operations (toggle, move, delete) | Select rows via checkbox column |
| Import / export | Toolbar buttons |
| Live reload | Edit a value in the UI; hit `GET /api/diag/feature-flags` again |
| Tenant gotcha (`IOptions<T>` vs `IOptionsSnapshot<T>`) | `GET /api/diag/io` with and without `X-Tenant-Id` |

Refer to the [UI Editor](../ui-editor/overview.md) section for detailed documentation of
each feature.

## Demo vs production

The demo `Program.cs` takes a couple of demo-only shortcuts:

| Demo | Production |
|------|-----------|
| `AppSettingsCredentialValidator` (one shared password) | Real `IDbConfigCredentialValidator` against your user store |
| Single password from `appsettings.json` | Hash-verified credentials (PBKDF2 / Argon2 / bcrypt) |
| `SchemaMode.CreateIfMissing` runs migrations on startup | Optional `SchemaMode.None` + DBA-applied SQL (`DbConfigMigrator.GenerateMigrationScript`) |
| Business endpoints unauthenticated | Wrap your own routes with the host's auth |
| In-memory mock for Stripe | Real Stripe SDK |

The composition pattern (`AddDbConfig` → `MapDbConfigAdmin`) is production-correct. Copy
that pattern; replace the validator with your own.
