---
sidebar_position: 6
---

import Screenshot from '@site/src/components/Screenshot';

# Authentication & authorization

DbConfig does not own identity. Both `MapDbConfigHttp` and `MapDbConfigUi` are
open by default and return a `RouteGroupBuilder` so hosts can compose any
auth pipeline they already have. An **opt-in built-in cookie login** plus a
**unified `MapDbConfigAdmin`** mount that gates the UI and HTTP API with one
cookie are also available.

There are five supported patterns, listed from "most built-in" to "least
invasive".

## 1. Unified `MapDbConfigAdmin` with built-in cookie login (recommended)

The common case — one call mounts UI and HTTP API under one prefix, one
cookie covers both:

```csharp
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();

app.MapDbConfigAdmin("/admin/dbconfig", opts =>
{
    opts.UseBuiltInLogin<MyValidator>();
});
// → UI  at /admin/dbconfig
// → API at /admin/dbconfig/api
```

The React app calls its own backend at `/admin/dbconfig/api/*` right after
sign-in with no separate auth dance — the cookie's `Path` defaults to the
unified prefix. `MapDbConfigAdmin` returns a
`DbConfigAdminEndpoints(Ui, Api)` record so consumers can chain per-surface
customizations:

```csharp
var endpoints = app.MapDbConfigAdmin("/admin/dbconfig", opts =>
    opts.UseBuiltInLogin<MyValidator>());

endpoints.Api.RequireRateLimiting("admin");
```

## 2. Open access (default)

The split-prefix shape works with no extra configuration:

```csharp
app.MapDbConfigHttp("/api/dbconfig");
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");
```

Both surfaces are reachable by anyone who can reach the process. Use only on
private networks or for local development.

## 3. Compose with the host's existing auth pipeline

```csharp
builder.Services.AddAuthentication(...).AddOpenIdConnect(...);
builder.Services.AddAuthorization(o =>
    o.AddPolicy("DbConfigAdmin", p => p.RequireRole("Admin")));

app.UseAuthentication();
app.UseAuthorization();

app.MapDbConfigHttp("/api/dbconfig").RequireAuthorization("DbConfigAdmin");
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig").RequireAuthorization("DbConfigAdmin");
```

This is the canonical pattern when the host already has OIDC, Windows Auth,
JWT bearer, or a similar scheme. Nothing about DbConfig changes — the route
groups behave like any other minimal-API group. Use this shape when UI and
API need to live at different prefixes (UI behind a CDN, API on a different
subdomain, etc.).

## 4. Built-in cookie login on split prefixes (`UseBuiltInLogin<T>`)

When the unified `MapDbConfigAdmin` (pattern 1) doesn't fit — for example,
the UI and HTTP API need different route prefixes — you can still use the
built-in login form on a split deployment.

```csharp
// 1. Implement IDbConfigCredentialValidator.
public sealed class MyValidator : IDbConfigCredentialValidator
{
    public async Task<ClaimsPrincipal?> ValidateAsync(
        string username, string password, CancellationToken ct)
    {
        // Look up the user, verify the hash, return a principal on success.
        if (!await _users.VerifyPasswordAsync(username, password, ct))
        {
            return null;
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username)],
            "DbConfigCookie");

        return new ClaimsPrincipal(identity);
    }
}

// 2. Register the validator (scoped — may inject DbContext etc.).
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();

// 3. Enable the built-in login on MapDbConfigUi.
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig", opts =>
{
    opts.UseBuiltInLogin<MyValidator>();
});
```

What this wires up (React-rendered):

<Screenshot light="/img/screenshots/15-login-form.png" dark="/img/screenshots/15-login-form-dark.png" alt="DbConfig built-in login form with username, password, and Sign in button" />


- `GET /admin/dbconfig/login` — serves the SPA `index.html`. The React app
  detects unauthenticated state via `GET /api/auth/status` and renders its
  own `<LoginPage>` component (shared theme tokens, dark mode, identical
  visual language to the rest of the dashboard).
- `POST /admin/dbconfig/api/auth/login` — accepts JSON
  `{ "username": "...", "password": "..." }`, calls your validator. On
  success, signs a cookie via `IDataProtectionProvider` and returns
  `200 OK { "ok": true }`. On failure, returns
  `401 Unauthorized { "error": "Invalid credentials" }`.
- `POST /admin/dbconfig/api/auth/logout` — clears the cookie, returns
  `200 OK { "ok": true }`.
- `GET /admin/dbconfig/api/auth/status` — returns
  `{ "authenticated": bool, "hasBuiltInLogin": true, "username": string? }`.
  Always reachable (no cookie required) so the SPA can decide whether to
  render `<LoginPage>` on boot.
- Endpoint filter on the route group — for unauthorized browser
  navigations, lets the SPA shell render (the React app handles the login
  flow itself); for unauthorized API/non-browser callers, returns `401`.

Manual curl example:

```bash
# Sign in
curl -i -X POST http://localhost:5000/admin/dbconfig/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"letmein"}'

# Check status (with cookie jar)
curl -i --cookie cookies.txt http://localhost:5000/admin/dbconfig/api/auth/status
```

Defaults: cookie name `dbconfig-auth`, expiry 7 days (sliding), path scoped to
the prefix, `HttpOnly`, `SameSite=Strict`, `Secure` flag auto-set on HTTPS.
Override via `opts.CookieName` / `opts.CookieExpireTimeSpan`.

**`returnUrl` handling:** because login is now driven by the SPA, there is no
server-side `returnUrl` parameter to validate. The browser stays on the page
that triggered the login; once authenticated, the React app simply re-checks
status and mounts the dashboard.

Shared cookie for the sibling HTTP API: capture the auto-wired filter and
pass it to a new `MapDbConfigHttp` overload:

```csharp
IDbConfigAuthorizationFilter? sharedFilter = null;
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig", opts =>
{
    opts.UseBuiltInLogin<MyValidator>();
    opts.CookiePath = "/";   // broaden so /api/dbconfig sees the cookie
    sharedFilter = opts.Authorization;
});

app.MapDbConfigHttp("/api/dbconfig", http => http.Authorization = sharedFilter);
```

For the simple case (UI + API under one prefix), prefer pattern 1 —
`MapDbConfigAdmin` does this wiring automatically.

## 5. Custom authorization filter

When neither a cookie nor a redirect fits — for example, header-based service
tokens, IP allowlists, or a custom JWT cookie — implement
`IDbConfigAuthorizationFilter` directly.

```csharp
public sealed class HeaderTokenFilter : IDbConfigAuthorizationFilter
{
    public Task<bool> IsAuthorizedAsync(HttpContext ctx)
    {
        var token = ctx.Request.Headers["X-Admin-Token"].FirstOrDefault();

        return Task.FromResult(string.Equals(token, _expected, StringComparison.Ordinal));
    }
}

app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig", opts =>
{
    opts.Authorization = new HeaderTokenFilter();
    opts.UnauthorizedRedirectUrl = "/my-existing-login"; // optional, browser only
});
```

Unauthorized requests get:
- The SPA shell (so the React `<LoginPage>` can render) if
  `UseBuiltInLogin<T>()` is set and the request looks like a browser
  navigation, OR
- 302 to `UnauthorizedRedirectUrl?returnUrl=...` for browsers if that is set, OR
- 401 in every other case.

The package ships `LocalRequestsOnlyAuthorizationFilter` as a ready-made
example (allows loopback addresses; convenient for dev).

## Comparison

| Pattern | Identity owner | Built-in form | Redirect on 401 |
|---|---|---|---|
| `MapDbConfigAdmin` + `UseBuiltInLogin<T>` | Consumer-implemented validator | yes (React) | SPA gates on `/api/auth/status` |
| Open access | (none) | n/a | n/a |
| `RequireAuthorization` | Host's existing auth pipeline | (consumer's) | (consumer's) |
| Split prefixes + `UseBuiltInLogin<T>` | Consumer-implemented validator | yes (React) | SPA gates on `/api/auth/status` |
| Custom filter + `UnauthorizedRedirectUrl` | Consumer-implemented filter | no | yes (consumer's URL) |

Pick pattern 1 when starting fresh. Pick pattern 3 if your host already has
an auth pipeline. Pick pattern 4 when the UI and HTTP API need different
prefixes but still want the built-in form. Pick pattern 5 for header-based
or IP-allowlist scenarios.

## Security boundaries

- The package never inspects the password — your validator is the security
  boundary. Hash and verify against your own user store.
- The cookie value is signed (not encrypted) via ASP.NET Data Protection.
  Configure key persistence (`PersistKeysToFileSystem` + cert) for
  multi-instance or restart-stable deployments — the default in-memory key
  ring rotates on every process restart.
- Bake content like the username into the cookie payload only if you need it;
  the package's default payload is `dbconfig|<username>|<utc-iso-timestamp>`.
- The built-in login is a single-factor flow with no rate limiting,
  account-lockout, or MFA. For internet-facing admin surfaces, prefer
  option 2 (compose with a hardened pipeline).
