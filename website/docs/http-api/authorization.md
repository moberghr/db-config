---
sidebar_position: 2
---

# Authorization

DbConfig defaults to **open access** and supports several composable auth patterns. The
full reference lives in [Authentication & authorization](../configuration/auth.md); this
page focuses on how authorization composes onto the HTTP API surface specifically.

## Recommended: unified mount with built-in cookie

For the common case (UI + HTTP API together under one prefix, gated by one signed
cookie), use `MapDbConfigAdmin`:

```csharp
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();

app.MapDbConfigAdmin("/admin/dbconfig", opts =>
    opts.UseBuiltInLogin<MyValidator>());
// UI at  /admin/dbconfig
// API at /admin/dbconfig/api  (gated by the same cookie)
```

Both the UI route group and the HTTP API route group share the same
`IDbConfigAuthorizationFilter`, auto-wired by `UseBuiltInLogin`. Unauthorized
non-browser callers receive `401`; browsers are redirected to `/admin/dbconfig/login`.

## Split deployment: separate `MapDbConfigHttp` + `MapDbConfigUi`

If the UI and HTTP API need different prefixes (UI behind a CDN, API on a different
subdomain), call them separately. Both return `RouteGroupBuilder` so any standard
`RequireAuthorization(...)` chain works:

```csharp
app.MapDbConfigHttp("/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");

app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");
```

`RequireAuthorization("DbConfigAdmin")` applies to all seven endpoints (flat list, flat
audit, get-single, upsert, delete, reload, per-key audit history).

## Composing with the host's existing auth

A minimal JWT bearer setup that protects both surfaces with one policy:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* issuer / audience / signing keys */ });

builder.Services.AddAuthorization(o =>
    o.AddPolicy("DbConfigAdmin", p =>
        p.RequireAuthenticatedUser()
         .RequireClaim("roles", "config-admin")));

app.UseAuthentication();
app.UseAuthorization();

app.MapDbConfigHttp("/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");
```

For Azure AD / Microsoft Entra, swap `AddJwtBearer` for `AddMicrosoftIdentityWebApi`.
For Windows Auth, use `RequireAuthenticatedUser()` with Windows authentication
middleware.

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

`scopeFilter` adds an endpoint filter to the group. Requests whose route `{scope}` does
not match the filter value receive `403 Forbidden`. The two route prefixes
(`/api/dbconfig` and `/api/dbconfig-shared`) keep the groups' URL spaces separate.

**The `/reload` endpoint** within each group is always allowed, regardless of the
`scopeFilter`. It fires the in-process reload signal and has no `{scope}` route
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

## Custom authorization filter

Skip cookies and policies entirely with a single-method
`IDbConfigAuthorizationFilter` — useful for header tokens, IP allowlists, or per-request
JWT checks that don't compose well with `[Authorize]`:

```csharp
public sealed class HeaderTokenFilter : IDbConfigAuthorizationFilter
{
    public Task<bool> IsAuthorizedAsync(HttpContext ctx) =>
        Task.FromResult(string.Equals(
            ctx.Request.Headers["X-Admin-Token"].FirstOrDefault(),
            _expected,
            StringComparison.Ordinal));
}

app.MapDbConfigHttp("/api/dbconfig", opts => opts.Authorization = new HeaderTokenFilter());
```

`LocalRequestsOnlyAuthorizationFilter` ships as a ready-made example (loopback only;
convenient for dev).

See [Authentication & authorization](../configuration/auth.md) for the full pattern
matrix, including the `UnauthorizedRedirectUrl` option for hosts with their own
existing login page.
