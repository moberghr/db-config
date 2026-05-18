---
sidebar_position: 6
---

# Authentication & authorization

DbConfig does not own identity. Both `MapDbConfigHttp` and `MapDbConfigUi` are
open by default and return a `RouteGroupBuilder` so hosts can compose any
auth pipeline they already have. v0.10.0 adds an **opt-in built-in cookie
login** plus a **unified `MapDbConfigAdmin`** mount that gates the UI and
HTTP API with one cookie.

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

The v0.9.0 shape continues to work unchanged:

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

What this wires up:

- `GET /admin/dbconfig/login` — renders a minimal HTML form (no external CSS/JS).
- `POST /admin/dbconfig/login` — calls your validator. On success, signs a
  cookie via `IDataProtectionProvider` and redirects to the validated
  `returnUrl`. On failure, redirects to `/login?error=1`.
- `POST /admin/dbconfig/logout` — clears the cookie and redirects to `/login`.
- Endpoint filter on the route group — redirects unauthorized browser
  requests to `/login?returnUrl=...` and returns `401` to API/non-browser
  callers.

Defaults: cookie name `dbconfig-auth`, expiry 7 days (sliding), path scoped to
the prefix, `HttpOnly`, `SameSite=Strict`, `Secure` flag auto-set on HTTPS.
Override via `opts.CookieName` / `opts.CookieExpireTimeSpan`.

**`returnUrl` safety:** the package rejects protocol-relative URLs
(`//evil.example/...`), CRLF injection, and any URL that doesn't start with
`/`. Invalid values fall back to the configured prefix.

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
- 302 to the built-in `/login` if `UseBuiltInLogin<T>()` is set, OR
- 302 to `UnauthorizedRedirectUrl?returnUrl=...` for browsers if that is set, OR
- 401 in every other case.

The package ships `LocalRequestsOnlyAuthorizationFilter` as a ready-made
example (allows loopback addresses; convenient for dev).

## Comparison

| Pattern | Identity owner | Built-in form | Redirect on 401 |
|---|---|---|---|
| `MapDbConfigAdmin` + `UseBuiltInLogin<T>` | Consumer-implemented validator | yes | yes (`/login`) |
| Open access | (none) | n/a | n/a |
| `RequireAuthorization` | Host's existing auth pipeline | (consumer's) | (consumer's) |
| Split prefixes + `UseBuiltInLogin<T>` | Consumer-implemented validator | yes | yes (`/login`) |
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
