---
sidebar_position: 7
---

import Screenshot from '@site/src/components/Screenshot';

# Audit Log page

The admin UI has an **Audit Log** tab next to **Entries** at the top. It surfaces the
full audit timeline across every app, environment, tenant, and key — including Delete
events whose entries no longer exist in the Entries grid.

<Screenshot light="/img/screenshots/16-audit-log.png" dark="/img/screenshots/16-audit-log-dark.png" alt="Audit log page showing color-coded action chips, app/env/tenant columns, key, old and new values, modified-by and timestamp" />

## When to use it vs the per-row History dialog

| Question | Page |
|---|---|
| "What happened to this specific key?" | Per-row History dialog (clock icon in Entries) |
| "Show me a deleted key's history" | Audit Log page — Entries grid no longer holds the row |
| "Who changed anything in the last hour?" | Audit Log page, sorted by `ModifiedUtc DESC` |
| "Show me only Inserts across all apps" | Audit Log page with the Action filter |
| "Show me a per-key diff between two values" | Per-row History dialog ([History and diff](./history-and-diff.md)) |

The two views are complementary. The per-row dialog is for deep-dives into one key's
timeline (it has the diff view, secret reveal per row, and "compare to previous"). The
Audit Log page is the global timeline — flat, filterable, no diff.

## What it shows

Each row in the Audit Log table displays:

- **Action chip** — color-coded:
  - **Insert** (green) — new key written
  - **Update** (blue) — existing key overwritten
  - **Delete** (red) — key removed (the only place these are visible after the fact)
  - **Read** (grey) — only present when `DbConfigOptions.AuditReads = true`
- **App / Env / Tenant** — scope coordinates; tenant column shows `Default` for global
  entries
- **Key** — the affected key, full path (e.g. `Notifications:Email:Smtp:Password`)
- **Old → New value** — both columns; per-row eye toggle reveals secret old/new values
  inline (independent per row). `OldValue` is null for Inserts; `NewValue` is null for
  Deletes; both null for Reads.
- **When** — `ModifiedUtc` formatted as `yyyy-MM-dd HH:mm:ss`
- **Who** — `ModifiedBy` (from `HttpContext.User.Identity.Name` at write time)

## Filters

The toolbar across the top supports:

| Filter | Behaviour |
|---|---|
| App | Equality match on `Scope` |
| Environment | Equality match on `Environment` |
| Tenant | Case-sensitive equality on `TenantId` (empty = global default) |
| Key prefix | Case-insensitive `StartsWith` match on `Key` |
| Action | Exact match on Insert / Update / Delete / Read |
| Take | Result cap, default 200, max 1000 |

Filters compose with AND semantics — each one narrows the result. A "Refresh" button
re-runs the query against the live database.

## Backing endpoint

The Audit Log page calls the flat audit endpoint:

```
GET {apiPrefix}/audit?scope=&environment=&tenantId=&keyPrefix=&action=&take=
```

Where `{apiPrefix}` is `/admin/dbconfig/api` for the unified `MapDbConfigAdmin` mount.
The endpoint returns a JSON array of `ConfigAuditEntry` ordered `ModifiedUtc DESC`,
with `action` serialized as its string name (`"Insert"`, `"Update"`, `"Delete"`,
`"Read"`) — not as the underlying integer.

See [Endpoints](../http-api/endpoints.md#get-audit--flat-audit-timeline) for the
full endpoint reference.

## Limitations

- **No diff.** Diffs are per-key and live in the [History dialog](./history-and-diff.md).
  The Audit Log page shows raw `OldValue` / `NewValue` text only.
- **Secrets still need reveal.** Secret old/new values are decrypted server-side but
  masked client-side. The per-row eye toggle reveals one row at a time.
- **No `?scopeFilter` propagation client-side.** When the route group was mounted with
  `MapDbConfigHttp(scopeFilter: "X")`, the endpoint enforces the filter server-side —
  cross-scope rows return `403`. The UI doesn't know about the server-side filter and
  may render a "no rows" message; this is correct.
- **Read audit rows can dominate.** When `AuditReads = true`, read rows generally
  outnumber mutation rows 10:1+ in a busy host. Filter `Action != Read` to focus on
  state changes, or prune read audits more aggressively
  ([Audit retention](../operations/audit-retention.md)).
