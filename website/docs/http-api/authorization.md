---
sidebar_position: 2
---

# Authorization

DbConfig ships **no authentication or authorization policy**. The host owns identity and
policy entirely. This is a non-negotiable design principle — the package never bakes in
an opinionated auth model.

## Host-owned auth pattern

`MapDbConfigHttp` returns a `RouteGroupBuilder`. Chain `RequireAuthorization(...)` on it to
protect all endpoints in the group:

```csharp
app.MapDbConfigHttp("/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");
```

`RequireAuthorization("DbConfigAdmin")` applies to all seven endpoints: list, get-single,
upsert, delete, reload, and audit-history.

The same pattern works for `MapDbConfigUi`:

```csharp
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");
```

Your host's standard authentication middleware (`UseAuthentication`, `UseAuthorization`)
handles the rest. DbConfig endpoints participate in ASP.NET Core's normal auth pipeline.

## Defining the policy

The policy name is yours to choose. A minimal JWT setup:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { ... });

builder.Services.AddAuthorization(o =>
    o.AddPolicy("DbConfigAdmin", p =>
        p.RequireAuthenticatedUser()
         .RequireClaim("roles", "config-admin")));

// ...

app.UseAuthentication();
app.UseAuthorization();

app.MapDbConfigHttp("/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");
```

For Azure AD / Microsoft Entra, replace `AddJwtBearer` with `AddMicrosoftIdentityWebApi`.
For Windows Auth, use `RequireAuthenticatedUser()` with Windows authentication middleware.

## Per-scope authorization with `scopeFilter`

When multiple teams own different scopes, use separate groups with different policies:

```csharp
// App team: read/write own scope only
app.MapDbConfigHttp("/api/dbconfig", scopeFilter: "PaymentService")
   .RequireAuthorization("AppTeamAdmin");

// Platform team: read/write shared scope only
app.MapDbConfigHttp("/api/dbconfig-shared", scopeFilter: "Shared")
   .RequireAuthorization("PlatformAdmin");
```

`scopeFilter` adds an endpoint filter to the group. Requests whose route `{appName}` does
not match the filter value receive `403 Forbidden`. The two route prefixes (`/api/dbconfig`
and `/api/dbconfig-shared`) keep the groups' URL spaces separate.

**The `/reload` endpoint** within each group is always allowed, regardless of the
`scopeFilter`. It fires the in-process reload signal and has no `{appName}` route
value to match against.

### Important: do not bypass `scopeFilter` by sharing policies

If you register both groups under the same policy, users with that policy can write to
either scope via either group endpoint. Use distinct policies per group to enforce the
team boundary:

```csharp
// Wrong: AppTeamAdmin can write to Shared via /api/dbconfig if no scopeFilter
app.MapDbConfigHttp("/api/dbconfig").RequireAuthorization("AppTeamAdmin");
app.MapDbConfigHttp("/api/dbconfig-shared").RequireAuthorization("AppTeamAdmin");

// Correct: separate filters AND separate policies
app.MapDbConfigHttp("/api/dbconfig", scopeFilter: "PaymentService")
   .RequireAuthorization("AppTeamAdmin");
app.MapDbConfigHttp("/api/dbconfig-shared", scopeFilter: "Shared")
   .RequireAuthorization("PlatformAdmin");
```

## Demo auth handler (NOT for production)

The `src/demo/DbConfig.Demo.WebApp` project uses a static API-key handler as a
demonstration of the composition pattern:

```csharp
// Demo Program.cs — NOT FOR PRODUCTION
builder.Services
    .AddAuthentication("ApiKey")
    .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>("ApiKey", null);

builder.Services.AddAuthorization(o =>
    o.AddPolicy("DbConfigAdmin", p => p.RequireAuthenticatedUser()));
```

The handler reads `X-Db-Config-Api-Key` from the request header and compares it against a
value from user secrets. It has no rate limiting, no nonce protection, and no expiry. It
exists to show the composition pattern, not to be copied into production.

See [Demo host](../getting-started/demo-host.md) for the full context.
