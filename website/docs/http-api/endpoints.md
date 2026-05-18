---
sidebar_position: 1
---

# Endpoints

`MapDbConfigHttp` registers seven endpoints under your chosen prefix. All endpoints
return camelCase JSON and share the route prefix you pass to `MapDbConfigHttp`.

## Endpoint reference

| Method | Path | Body | Success | Description |
|--------|------|------|---------|-------------|
| `GET` | `/` | — | 200 / 403 | Flat-query all entries with optional filters |
| `GET` | `/{appName}/{environment}` | — | 200 | List all entries for a scope |
| `GET` | `/{appName}/{environment}/{*key}` | — | 200 / 404 | Get a single entry |
| `PUT` | `/{appName}/{environment}/{*key}` | `UpsertEntryRequest` | 204 | Create or update an entry |
| `DELETE` | `/{appName}/{environment}/{*key}` | — | 204 | Delete an entry |
| `POST` | `/reload` | — | 204 | Trigger immediate in-process reload |
| `GET` | `/{appName}/{environment}/audit/{*key}` | — | 200 | Get audit history for a key |

All paths are relative to the prefix passed to `MapDbConfigHttp`. With prefix
`/api/dbconfig`, the list endpoint is `GET /api/dbconfig/MyApp/Production`.

## `GET /` — flat query (v0.10.0+)

Returns every entry across all apps, environments, and tenants with optional
query-string filters. Used by the admin UI on first paint so operators see data
immediately without having to enter `AppName` + `Environment` first.

**Optional query string filters (AND semantics — each narrows the result):**

| Param | Behaviour |
|---|---|
| `appName` | Equality match on `AppName` |
| `environment` | Equality match on `Environment` |
| `tenantId` | Case-sensitive equality on `TenantId`. Empty string matches global defaults |
| `keyPrefix` | Case-insensitive `StartsWith` match on `Key` |
| `take` | Result cap. Default `1000`, max `10000`. Out-of-range values are clamped |

**Ordering:** `(AppName, Environment, TenantId, Key)` ascending. Stable across
repeated calls with the same data — safe for paging.

**Scope filter:** when the group was mounted with `MapDbConfigHttp(scopeFilter: "X")`,
the endpoint forces `appName=X`. A caller-supplied `appName` that mismatches the filter
returns `403 Forbidden`; an omitted `appName` is silently substituted with the filter.

**Response:** `200 OK` with a JSON array of `ConfigEntry` (empty array when no rows
match — never `404`). Secret entries are returned **decrypted** in plaintext, just
like the existing single-key `GET` endpoint.

```bash
# All entries (capped at 1000)
curl http://localhost:5000/api/dbconfig/

# Narrow by app + key prefix
curl "http://localhost:5000/api/dbconfig/?appName=MyApp&keyPrefix=Stripe:"

# Per-tenant view
curl "http://localhost:5000/api/dbconfig/?tenantId=Acme&take=50"
```

## `GET /{appName}/{environment}` — list entries

Returns all `ConfigEntry` records for the given scope, ordered by `Key` ascending.

**Optional query string:**

`?includeScopes=Shared,PlatformDefaults` — includes entries from additional scopes with
the same precedence rules as `DbConfigOptions.IncludeScopes`. Each returned entry includes
its source `AppName` so the UI can show scope badges.

```bash
curl http://localhost:5000/api/dbconfig/MyApp/Production \
  -H "Authorization: Bearer <token>"

# With shared scopes
curl "http://localhost:5000/api/dbconfig/MyApp/Production?includeScopes=Shared,PlatformDefaults" \
  -H "Authorization: Bearer <token>"
```

Response (200):

```json
[
  {
    "appName": "MyApp",
    "environment": "Production",
    "key": "Database:ConnectionString",
    "value": "Server=...",
    "isSecret": true,
    "modifiedUtc": "2026-05-17T12:00:00Z",
    "modifiedBy": "alice@example.com"
  }
]
```

## `GET /{appName}/{environment}/{*key}` — get single entry

Returns the single `ConfigEntry` identified by `(appName, environment, key)`, or `404 Not
Found` if no such entry exists. The `{*key}` catch-all route segment normalizes forward
slashes to `:`, so `/Database/ConnectionString` and `/Database:ConnectionString` are
equivalent.

```bash
curl http://localhost:5000/api/dbconfig/MyApp/Production/Database:ConnectionString \
  -H "Authorization: Bearer <token>"
```

Response (200):

```json
{
  "appName": "MyApp",
  "environment": "Production",
  "key": "Database:ConnectionString",
  "value": "Server=prod-sql;Database=mydb;Integrated Security=true",
  "isSecret": true,
  "modifiedUtc": "2026-05-17T12:00:00Z",
  "modifiedBy": "alice@example.com"
}
```

## `PUT /{appName}/{environment}/{*key}` — upsert

Creates a new entry or overwrites an existing one. Last-writer-wins on concurrent upserts
to the same key. After a successful write, fires `IDbConfigReloadSignal.Trigger()`.

```bash
curl -X PUT http://localhost:5000/api/dbconfig/MyApp/Production/Feature:DarkMode \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"value": "true", "isSecret": false}'
```

Request body (`UpsertEntryRequest`):

```json
{
  "value": "the value to store",
  "isSecret": false
}
```

`value` may be null to store an explicit null entry. `isSecret` defaults to `false` if
omitted.

Response: `204 No Content`.

## `DELETE /{appName}/{environment}/{*key}` — delete

Removes the entry. No-op if the key does not exist. After a successful delete (including
no-op), fires `IDbConfigReloadSignal.Trigger()`.

```bash
curl -X DELETE http://localhost:5000/api/dbconfig/MyApp/Production/Feature:DarkMode \
  -H "Authorization: Bearer <token>"
```

Response: `204 No Content`.

## `POST /reload` — trigger immediate reload

Forces the in-process configuration provider to reload immediately without waiting for the
next poll interval. Returns `204 No Content`. The mutation endpoints (`PUT`, `DELETE`)
already call this automatically; use this endpoint if you need to force a reload after
an out-of-band database change.

```bash
curl -X POST http://localhost:5000/api/dbconfig/reload \
  -H "Authorization: Bearer <token>"
```

Note: the route is `/reload` with no `/{appName}/{environment}` prefix. It affects only
the in-process provider for this host.

## `GET /{appName}/{environment}/audit/{*key}` — audit history

Returns the audit history for a specific key, ordered most-recent-first.

`?take=N` — number of rows to return. Default 50, capped at 500.

```bash
curl "http://localhost:5000/api/dbconfig/MyApp/Production/audit/Database:ConnectionString?take=20" \
  -H "Authorization: Bearer <token>"
```

Response (200):

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "appName": "MyApp",
    "environment": "Production",
    "key": "Database:ConnectionString",
    "oldValue": "Server=old-sql;...",
    "newValue": "Server=new-sql;...",
    "isSecret": true,
    "action": "Update",
    "modifiedUtc": "2026-05-17T14:30:00Z",
    "modifiedBy": "alice@example.com"
  }
]
```

`oldValue` and `newValue` are decrypted plaintext even for secret entries.
`oldValue` is null for `Insert` actions. `newValue` is null for `Delete` actions. Both are
null for `Read` actions (when read auditing is enabled).

## Status code summary

| Code | When |
|------|------|
| 200 | Successful GET |
| 204 | Successful PUT, DELETE, or POST /reload |
| 400 | `?take` exceeds 500 on audit endpoint |
| 403 | `scopeFilter` mismatch — request appName does not match the group's filter |
| 404 | Key not found on single-entry GET |
| 500 | Unexpected store error |

## Authorization

All endpoints inherit the authorization policy applied to the `RouteGroupBuilder` returned
by `MapDbConfigHttp`. See [Authorization](./authorization.md) for the full pattern.
