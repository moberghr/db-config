---
sidebar_position: 1
---

# Endpoints

`MapDbConfigHttp` registers seven endpoints under your chosen prefix. All endpoints
return camelCase JSON and share the route prefix you pass to `MapDbConfigHttp` (or
`MapDbConfigAdmin`'s `{prefix}/api` derivative).

## Endpoint reference

| Method | Path | Body | Success | Description |
|--------|------|------|---------|-------------|
| `GET` | `/` | — | 200 / 403 | Flat-query all entries with optional filters |
| `GET` | `/audit` | — | 200 / 403 | Flat-query audit timeline with optional filters |
| `GET` | `/{scope}/{environment}/{*key}` | — | 200 / 404 | Get a single entry |
| `PUT` | `/{scope}/{environment}/{*key}` | `UpsertEntryRequest` | 204 | Create or update an entry |
| `DELETE` | `/{scope}/{environment}/{*key}` | — | 204 | Delete an entry |
| `POST` | `/reload` | — | 204 | Trigger immediate in-process reload |
| `GET` | `/{scope}/{environment}/audit/{*key}` | — | 200 | Get audit history for a key |

All paths are relative to the prefix passed to `MapDbConfigHttp`. With prefix
`/api/dbconfig`, the flat-query endpoint is `GET /api/dbconfig/`. With the unified
`MapDbConfigAdmin("/admin/dbconfig", ...)`, it is `GET /admin/dbconfig/api/`.

## `GET /` — flat entries query

Returns every entry across all apps, environments, and tenants with optional
query-string filters. Used by the admin UI on first paint so operators see data
immediately without having to enter `Scope` + `Environment` first.

**Optional query string filters (AND semantics — each narrows the result):**

| Param | Behaviour |
|---|---|
| `scope` | Equality match on `Scope` |
| `environment` | Equality match on `Environment` |
| `tenantId` | Case-sensitive equality on `TenantId`. Empty string matches global defaults |
| `keyPrefix` | Case-insensitive `StartsWith` match on `Key` |
| `take` | Result cap. Default `1000`, max `10000`. Out-of-range values are clamped |

**Ordering:** `(Scope, Environment, TenantId, Key)` ascending. Stable across
repeated calls with the same data — safe for paging.

**Scope filter:** when the group was mounted with `MapDbConfigHttp(scopeFilter: "X")`,
the endpoint forces `scope=X`. A caller-supplied `scope` that mismatches the filter
returns `403 Forbidden`; an omitted `scope` is silently substituted with the filter.

**Response:** `200 OK` with a JSON array of `ConfigEntry` (empty array when no rows
match — never `404`). Secret entries are returned **decrypted** in plaintext, just
like the existing single-key `GET` endpoint.

```bash
# All entries (capped at 1000)
curl http://localhost:5000/admin/dbconfig/api/

# Narrow by app + key prefix
curl "http://localhost:5000/admin/dbconfig/api/?scope=MyApp&keyPrefix=Stripe:"

# Per-tenant view
curl "http://localhost:5000/admin/dbconfig/api/?tenantId=Acme&take=50"
```

## `GET /audit` — flat audit timeline

Returns the global audit log across all apps, environments, tenants, and keys with
optional filters. Backs the new Audit Log tab in the admin UI so Delete events for
entries that no longer exist remain visible.

**Optional query string filters (AND semantics):**

| Param | Behaviour |
|---|---|
| `scope` | Equality match on `Scope` |
| `environment` | Equality match on `Environment` |
| `tenantId` | Case-sensitive equality on `TenantId` |
| `keyPrefix` | Case-insensitive `StartsWith` match on `Key` |
| `action` | Filter to a single action: `Insert`, `Update`, `Delete`, `Read` |
| `take` | Result cap. Default `200`, max `1000` |

**Ordering:** `ModifiedUtc DESC` (most recent first).

**Scope filter:** same enforcement as `GET /` — a mismatch on `scope` returns
`403 Forbidden`.

**Response:** `200 OK` with a JSON array of `ConfigAuditEntry`. `Action` is serialized
as its string name (`"Insert"`, `"Update"`, `"Delete"`, `"Read"`), not the underlying
integer.

```bash
# Last 200 audit events overall
curl http://localhost:5000/admin/dbconfig/api/audit

# Only deletes, last 50
curl "http://localhost:5000/admin/dbconfig/api/audit?action=Delete&take=50"
```

## `GET /{scope}/{environment}/{*key}` — get single entry

Returns the single `ConfigEntry` identified by `(scope, environment, key)`, or `404 Not
Found` if no such entry exists. The `{*key}` catch-all route segment normalizes forward
slashes to `:`, so `/Database/ConnectionString` and `/Database:ConnectionString` are
equivalent.

```bash
curl http://localhost:5000/admin/dbconfig/api/MyApp/Production/Database:ConnectionString \
  -b "dbconfig-auth=$COOKIE"
```

Response (200):

```json
{
  "scope": "MyApp",
  "environment": "Production",
  "tenantId": "",
  "key": "Database:ConnectionString",
  "value": "Server=prod-sql;Database=mydb;Integrated Security=true",
  "isSecret": true,
  "modifiedUtc": "2026-05-17T12:00:00Z",
  "modifiedBy": "alice@example.com"
}
```

The `tenantId` defaults to the global default sentinel (`""`). Use the `?tenantId=` query
string parameter on this endpoint to target a tenant-specific row.

## `PUT /{scope}/{environment}/{*key}` — upsert

Creates a new entry or overwrites an existing one. Last-writer-wins on concurrent upserts
to the same key. After a successful write, fires `IDbConfigReloadSignal.Trigger()`.

```bash
curl -X PUT http://localhost:5000/admin/dbconfig/api/MyApp/Production/Feature:DarkMode \
  -H "Content-Type: application/json" \
  -b "dbconfig-auth=$COOKIE" \
  -d '{"value": "true", "isSecret": false, "tenantId": ""}'
```

Request body (`UpsertEntryRequest`):

```json
{
  "value": "the value to store",
  "isSecret": false,
  "tenantId": ""
}
```

`value` may be null to store an explicit null entry. `isSecret` defaults to `false` if
omitted. `tenantId` defaults to `""` (global default).

Response: `204 No Content`.

## `DELETE /{scope}/{environment}/{*key}` — delete

Removes the entry. No-op if the key does not exist. Targets the row identified by
`{scope}/{environment}/{key}` and the `?tenantId=` query string parameter (default
`""`). After a successful delete (including no-op), fires
`IDbConfigReloadSignal.Trigger()`.

```bash
curl -X DELETE "http://localhost:5000/admin/dbconfig/api/MyApp/Production/Feature:DarkMode?tenantId=Acme" \
  -b "dbconfig-auth=$COOKIE"
```

Response: `204 No Content`.

## `POST /reload` — trigger immediate reload

Forces the in-process configuration provider to reload immediately without waiting for the
next poll interval. Returns `204 No Content`. The mutation endpoints (`PUT`, `DELETE`)
already call this automatically; use this endpoint if you need to force a reload after
an out-of-band database change.

```bash
curl -X POST http://localhost:5000/admin/dbconfig/api/reload \
  -b "dbconfig-auth=$COOKIE"
```

Note: the route is `/reload` with no `/{scope}/{environment}` prefix. It affects only
the in-process provider for this host.

## `GET /{scope}/{environment}/audit/{*key}` — per-key audit history

Returns the audit history for a single specific key, ordered most-recent-first. Backs the
per-row history dialog in the UI.

`?take=N` — number of rows to return. Default 50, capped at 500.

```bash
curl "http://localhost:5000/admin/dbconfig/api/MyApp/Production/audit/Database:ConnectionString?take=20" \
  -b "dbconfig-auth=$COOKIE"
```

Response (200):

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "scope": "MyApp",
    "environment": "Production",
    "tenantId": "",
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
null for `Read` actions (when read auditing is enabled). `action` is serialized as its
string name.

## Status code summary

| Code | When |
|------|------|
| 200 | Successful GET |
| 204 | Successful PUT, DELETE, or POST /reload |
| 400 | `?take` exceeds the per-endpoint cap |
| 401 | Authorization filter rejected the request (built-in cookie or custom filter) |
| 403 | `scopeFilter` mismatch — request scope does not match the group's filter |
| 404 | Key not found on single-entry GET |
| 500 | Unexpected store error |

## Authorization

All endpoints inherit the authorization model applied to the route group. See
[Authorization](./authorization.md) for the four supported patterns (open access, built-in
cookie, custom filter, host pipeline composition).
