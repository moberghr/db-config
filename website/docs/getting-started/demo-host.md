---
sidebar_position: 3
---

# Demo host

The `src/demo/DbConfig.Demo.WebApp` project is a minimal ASP.NET Core 8 app that wires
up the full DbConfig stack — SQL Server provider, HTTP API, embedded UI, EF Core
migrations on startup, and an API-key authentication handler.

Use it to explore the features locally before integrating DbConfig into your own
application.

## Clone and run

### Prerequisites

- .NET 8 SDK
- SQL Server instance (local, Docker, or Azure SQL)
- Node.js is **not** required — the React UI is pre-built into the NuGet package

### 1. Clone the repo

```bash
git clone https://github.com/moberg/db-config.git
cd db-config
```

### 2. Set the connection string

The demo reads its SQL Server connection string from .NET user secrets to avoid committing
credentials. Set it once:

```bash
dotnet user-secrets set "ConnectionStrings:DbConfig" \
  "Server=localhost;Database=DbConfigDemo;Integrated Security=true;TrustServerCertificate=true" \
  --project src/demo/DbConfig.Demo.WebApp
```

Replace the connection string with your actual server details.

### 3. Set the admin API key

The demo uses a static API-key auth handler (see below). Set an API key:

```bash
dotnet user-secrets set "DbConfigDemo:AdminApiKey" "my-local-dev-key" \
  --project src/demo/DbConfig.Demo.WebApp
```

### 4. Run the demo

```bash
dotnet run --project src/demo/DbConfig.Demo.WebApp
```

The demo applies EF Core migrations automatically on startup (`ctx.Database.MigrateAsync()`),
so you do not need to run `dotnet ef database update` manually.

Open `http://localhost:5000` to see the root page, which shows the API and UI URLs.

### 5. Open the editor UI

Navigate to `http://localhost:5000/admin/dbconfig` and include the API key as a request
header. The easiest way in a browser is to use a browser extension that adds custom headers
(e.g. ModHeader), or open the URL from `curl` and redirect to a local proxy.

Alternatively, use the HTTP API directly:

```bash
# Write an entry
curl -X PUT http://localhost:5000/api/dbconfig/DbConfigDemo/Development/MyFeature \
  -H "Content-Type: application/json" \
  -H "X-Db-Config-Api-Key: my-local-dev-key" \
  -d '{"value": "enabled", "isSecret": false}'

# Read it back (flat query, narrowed to this app + env)
curl "http://localhost:5000/api/dbconfig/?appName=DbConfigDemo&environment=Development" \
  -H "X-Db-Config-Api-Key: my-local-dev-key"
```

## API key auth handler

The demo registers a custom `AuthenticationHandler` that reads `X-Db-Config-Api-Key` from
the request header and compares it against the configured value. This is wired in
`Program.cs`:

```csharp
builder.Services
    .AddAuthentication("ApiKey")
    .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>("ApiKey", null);

builder.Services.AddAuthorization(o =>
    o.AddPolicy("DbConfigAdmin", p => p.RequireAuthenticatedUser()));
```

The UI and HTTP endpoints are then protected by `RequireAuthorization("DbConfigAdmin")`.

:::warning
This auth handler is for local development and demo purposes only. It stores the key in
application configuration (user secrets in dev) and does no rate limiting or nonce
protection. Do not copy this pattern into production. Production hosts should use JWT
bearer tokens, Azure AD, Windows Auth, or another identity provider.
:::

## What to explore in the demo

| Feature | Where to find it |
|---------|-----------------|
| Entries list with scope badge | `http://localhost:5000/admin/dbconfig` |
| Create / edit / delete entries | Click a row or use the toolbar |
| Secret masking + reveal | Create an entry with `IsSecret = true` |
| Audit history per row | Click the clock icon on any row |
| Diff view | Open history, then click "Compare to previous" |
| Bulk operations | Select rows with the checkbox column |
| Import / export | Use the Import/Export toolbar buttons |
| Scope selector | Top-right of the UI |

Refer to the [UI Editor](../ui-editor/overview.md) section for detailed documentation of
each feature.

## Demo vs production

The demo `Program.cs` intentionally takes shortcuts:

| Demo | Production |
|------|-----------|
| Programmatic `MigrateAsync()` at startup | CLI `dotnet ef database update` in CI/CD |
| Static API-key auth handler | JWT / Azure AD / Windows Auth |
| UI not behind `RequireAuthorization` | Protect UI the same way as the API |
| Single scope, no `IncludeScopes` | Multi-scope per [Scopes](../configuration/scopes.md) |

The composition pattern (`AddDbConfig` → `MapDbConfigHttp` → `MapDbConfigUi`) is
production-correct. Copy that pattern; replace the auth handler with your own.
