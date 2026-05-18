# DbConfig Demo Host

> **NOT FOR PRODUCTION.** This host uses a plaintext API-key header for auth. It is a development
> smoke-test harness, not a hardened deployment target.

## What this is

A minimal ASP.NET Core host that wires up all five DbConfig packages:

- **DbConfig.Core** — `IConfiguration` provider, polling reload
- **DbConfig.Provider.SqlServer** — EF Core SQL Server store
- **DbConfig.Http** — JSON CRUD API at `/api/dbconfig`
- **DbConfig.Ui** — embedded React SPA at `/admin/dbconfig`

On startup it applies pending EF migrations automatically, then begins polling the
SQL Server store every 10 seconds.

## Credentials setup (required before running)

`appsettings.json` contains only placeholder values — the connection string and API key
**must** be supplied via user secrets (preferred) or environment variables. Without them
the host throws `InvalidOperationException` on startup.

### Option A — dotnet user secrets (recommended for local dev)

```bash
dotnet user-secrets init --project src/demo/DbConfig.Demo.WebApp
dotnet user-secrets set "ConnectionStrings:DbConfig" "Server=localhost,1433;Database=DbConfigDemo;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true" --project src/demo/DbConfig.Demo.WebApp
dotnet user-secrets set "DbConfigDemo:AdminApiKey" "your-dev-key" --project src/demo/DbConfig.Demo.WebApp
```

### Option B — `appsettings.Development.Local.json` (git-ignored)

Create `src/demo/DbConfig.Demo.WebApp/appsettings.Development.Local.json` (already in `.gitignore`):

```json
{
  "ConnectionStrings": {
    "DbConfig": "Server=localhost,1433;Database=DbConfigDemo;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true"
  },
  "DbConfigDemo": {
    "AdminApiKey": "your-dev-key"
  }
}
```

## Run locally

**Prerequisite:** SQL Server reachable at `localhost,1433` (see Docker Compose snippet below).

```bash
dotnet run --project src/demo/DbConfig.Demo.WebApp
```

The host listens on `http://localhost:5000` by default.

### Start SQL Server with Docker Compose

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: "YourStrong!Passw0rd"
      ACCEPT_EULA: "Y"
    ports:
      - "1433:1433"
```

```bash
docker compose up -d
```

## Call the API with the API key

All `/api/dbconfig` endpoints require the `X-Db-Config-Api-Key` header.

```bash
# List entries for app "DbConfigDemo" in env "Development"
curl -H "X-Db-Config-Api-Key: your-dev-key" \
     http://localhost:5000/api/dbconfig/DbConfigDemo/Development

# Upsert a key
curl -X PUT \
     -H "X-Db-Config-Api-Key: your-dev-key" \
     -H "Content-Type: application/json" \
     -d '{"value":"hello","isSecret":false}' \
     http://localhost:5000/api/dbconfig/DbConfigDemo/Development/MyKey

# Trigger an immediate reload
curl -X POST \
     -H "X-Db-Config-Api-Key: your-dev-key" \
     http://localhost:5000/api/dbconfig/reload
```

## Open the UI

Navigate to `http://localhost:5000/admin/dbconfig`. The React SPA loads without auth;
the API calls it makes will include the key from a future login flow (out of scope for v1).

## Multi-tenant demo

The demo includes a `/demo/whoami` endpoint that reads the current tenant from the
`X-Tenant-Id` header and returns its bound options.

First, seed some tenant-specific config via the API:

```bash
# Global defaults
curl -X PUT http://localhost:5000/api/dbconfig/DbConfigDemo/Development/DemoTenant:DisplayName \
  -H "X-Db-Config-Api-Key: $KEY" \
  -d '{"value":"Default Display Name","isSecret":false,"tenantId":""}'

# Acme's override
curl -X PUT http://localhost:5000/api/dbconfig/DbConfigDemo/Development/DemoTenant:DisplayName \
  -H "X-Db-Config-Api-Key: $KEY" \
  -d '{"value":"Acme Corp","isSecret":false,"tenantId":"Acme"}'

curl -X PUT http://localhost:5000/api/dbconfig/DbConfigDemo/Development/DemoTenant:StripeApiKey \
  -H "X-Db-Config-Api-Key: $KEY" \
  -d '{"value":"sk_live_acme_123","isSecret":true,"tenantId":"Acme"}'
```

Then call `/demo/whoami` with different tenant headers:

```bash
# No tenant header → global defaults
curl http://localhost:5000/demo/whoami

# Acme tenant → Acme's overrides + global fallbacks
curl -H "X-Tenant-Id: Acme" http://localhost:5000/demo/whoami
```

### How the resolver pattern works

The `DemoTenantResolver` in `Program.cs` implements the `ITenantResolver` interface and
reads the `X-Tenant-Id` header from `IHttpContextAccessor`. It is registered via
`b.AddTenantResolver<DemoTenantResolver>()` inside the `AddDbConfig` call.

The configuration provider calls `Resolve()` on every `IConfiguration[key]` read, returning
a tenant-specific value if one exists or falling back to the global default. Standard
`IOptionsSnapshot<T>` (scoped per-request) rides on top of this transparently — no custom
options API needed.

> **Note:** `IOptions<T>` is singleton-cached and binds once at startup when no request scope
> exists (resolver returns null). Use `IOptionsSnapshot<T>` for any tenant-aware options types.

Production hosts typically extract tenant identity from a JWT claim, route segment, subdomain,
or other source — db-config is unopinionated about the resolution source. The host owns the
resolver; db-config ships only the `ITenantResolver` interface.

## Security note

The API key and connection string must be supplied via user secrets or environment variables —
they are **not** committed to source control. **Never use the demo auth pattern outside a local
development environment.** For production, integrate with your existing identity provider and
apply `RequireAuthorization` with a real policy.
