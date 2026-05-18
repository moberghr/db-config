---
sidebar_position: 6
---

# Authentication & authorization

DbConfig does not own identity. Both `MapDbConfigHttp` and `MapDbConfigUi` are
open by default and return a `RouteGroupBuilder` so hosts can compose any
auth pipeline they already have. v0.10.0 adds an **opt-in built-in cookie
login** for the UI surface so demos and small deployments can avoid wiring
their own scheme.

There are four supported patterns, listed from "least invasive" to "most
built-in".

## 1. Open access (default)

The v0.9.0 shape continues to work unchanged:

```csharp
app.MapDbConfigHttp("/api/dbconfig");
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");
```

Both surfaces are reachable by anyone who can reach the process. Use only on
private networks or for local development.

## 2. Compose with the host's existing auth pipeline

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
groups behave like any other minimal-API group.

## 3. Built-in cookie login (`UseBuiltInLogin<T>`)

For hosts without an existing identity story (small services, on-prem tools,
internal admin sites), `Moberg.DbConfig.Ui` ships a cookie scheme and login
form you can opt into.

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

`MapDbConfigHttp` has no built-in auth surface — gate it via
`RequireAuthorization(...)` or rely on an existing scheme.

## 4. Custom authorization filter

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
| Open access | (none) | n/a | n/a |
| `RequireAuthorization` | Host's existing auth pipeline | (consumer's) | (consumer's) |
| `UseBuiltInLogin<T>()` | Consumer-implemented validator | yes | yes (`/login`) |
| Custom filter + `UnauthorizedRedirectUrl` | Consumer-implemented filter | no | yes (consumer's URL) |

Pick option 2 if your host already has an auth pipeline. Pick option 3 if it
doesn't and you want a quick admin login. Pick option 4 for header-based or
IP-allowlist scenarios.

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
