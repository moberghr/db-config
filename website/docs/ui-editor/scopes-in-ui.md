---
sidebar_position: 6
---

import Screenshot from '@site/src/components/Screenshot';

# Scopes in UI

The scope selector controls which entries the UI loads and which scope new entries are
written to.

## Scope selector

<Screenshot light="/img/screenshots/09-scope-selector.png" dark="/img/screenshots/09-scope-selector-dark.png" alt="Scope selector showing AppName, Environment, and IncludeScopes fields" />

The scope selector panel has three fields:

- **AppName** — the primary scope. New entries are written here unless the create dialog
  overrides it. Corresponds to `DbConfigOptions.AppName` on the server.
- **Environment** — the deployment environment (`Production`, `Development`, `Staging`,
  etc.). Filters all queries to this environment.
- **IncludeScopes** — comma-separated list of additional `AppName` values to include.
  Uses the same precedence rules as `DbConfigOptions.IncludeScopes`: the primary
  `AppName` always wins. Example: `PlatformDefaults,Shared`.

The selector values are persisted to `localStorage`. Refreshing the page restores the
previous selection.

## View mode toggle

A toggle above the entries table switches between three view modes:

| Mode | What it shows |
|------|--------------|
| **Mine** | Only entries from the primary `AppName` |
| **Shared** | Only entries from the `IncludeScopes` (not the primary AppName) |
| **All** | All entries from all configured scopes, merged with precedence |

The `All` view is the most useful for debugging: it shows which scope each value comes
from and makes shadowed/overridden entries visible.

## Scope badge

In the `All` or `Shared` views, the **Scope** column shows a colored badge with the source
`AppName` for each row. The badge color is consistent per scope name across the session.

Rows from included scopes have their edit and delete buttons disabled. The title attribute
on the disabled button explains:

> This entry belongs to scope "Shared". Edit it from that scope's admin UI.

This is a read-only view of cross-scope entries. To write to them, use the HTTP API group
configured with `scopeFilter: "Shared"` or navigate to the admin UI for that scope.

## Selector state and server-side options

The scope selector is a client-side control — the values you type are used to construct
the HTTP request (`GET /api/dbconfig/{appName}/{env}?includeScopes=...`). The UI does not
enforce any server-side `IncludeScopes` configuration; you can query any scope you have
read access to.

If your host has `scopeFilter: "PaymentService"` set on the API group, queries for other
`AppName` values will return `403`. The selector lets you try any scope; the server
enforces what you are allowed to read.

## Tree view

In addition to the flat table, the UI offers a **tree view** that groups entries by their
colon-separated key prefix. Toggle between views using the **Flat | Tree** segmented
control in the toolbar.

<Screenshot light="/img/screenshots/11-tree-view.png" dark="/img/screenshots/11-tree-view-dark.png" alt="Tree view showing entries grouped by key prefix with expand/collapse chevrons" />

In tree view, keys like `Stripe:Payment:Foo`, `Stripe:Payment:Bar`, and `Stripe:Auth:Baz`
are grouped under a `Stripe` parent node with `Payment (2)` and `Auth (1)` child groups.
Each group row shows its segment name and a count of descendant entries in parentheses.

**Expand and collapse:**
- Click the chevron on a group row to expand or collapse it
- The toolbar shows **Expand all** and **Collapse all** buttons to manage all groups at once
- Groups start collapsed by default and reset to collapsed when the scope changes

**Leaf row actions:**
All row actions (Edit, Delete, History, checkbox for bulk) are available on leaf rows
exactly as they are in the flat view. Selection state is shared between flat and tree
views — switching views preserves which entries are selected.

The Flat | Tree preference is persisted to `localStorage` via the scope store.

## Tenant scoping

The scope selector has a **Tenant** input alongside AppName, Environment, and IncludeScopes.

<Screenshot light="/img/screenshots/12-tenant-selector.png" dark="/img/screenshots/12-tenant-selector-dark.png" alt="Scope selector with Tenant field filled with Acme" />

The **Tenant** field controls which tenant's entries are loaded:

- **Empty (default):** loads global default entries (`TenantId = ""`). All entries show a **Default** badge in the Tenant column.
- **Tenant name (e.g. `Acme`):** loads only that tenant's overrides. Rows show a colored badge with the tenant name.

The Tenant value is persisted to `localStorage` along with the other scope fields. Refreshing the page restores the previous tenant selection.

### Tenant column badges

The entries table shows a **Tenant** column with a badge for each row:

- **Default** — grey badge; the entry applies to all tenants that do not have a specific override.
- **Tenant name** (e.g. `Acme`) — colored badge; the entry is a tenant-specific override.

<Screenshot light="/img/screenshots/13-tenant-entries-view.png" dark="/img/screenshots/13-tenant-entries-view-dark.png" alt="Entries table with Default and Acme tenant badges" />

### Creating a tenant-specific entry

When a tenant is selected in the scope selector, the **New Entry** dialog pre-fills the **Tenant ID** field with that tenant. This prevents accidental writes to the global default when working in a tenant-scoped session.

<Screenshot light="/img/screenshots/14-create-with-tenant-dialog.png" dark="/img/screenshots/14-create-with-tenant-dialog-dark.png" alt="Create Entry dialog with TenantId field showing Acme" />

The Tenant ID field in the dialog can be changed before submitting — this lets you write to a different tenant than the one currently selected in the scope selector (useful for admin workflows). Leaving it empty writes a global default entry.

:::note
The entries table does not currently support cross-tenant copy or move workflows. To migrate an entry from global defaults to a specific tenant (or between tenants), use the HTTP API `PUT /{app}/{env}/{*key}` endpoint with `tenantId` in the request body, then delete the source entry.
:::
