---
sidebar_position: 1
---

import Screenshot from '@site/src/components/Screenshot';

# UI editor overview

DbConfig ships a full-featured React editor UI as embedded static assets in
`Moberg.DbConfig.Ui`. The unified `MapDbConfigAdmin` call mounts the UI plus its HTTP API
under one prefix in your ASP.NET Core application; the split `MapDbConfigUi` +
`MapDbConfigHttp` calls remain available for hosts that need different prefixes.

## Mounting the UI

### Recommended: unified mount

```csharp
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();

app.MapDbConfigAdmin("/admin/dbconfig", opts =>
    opts.UseBuiltInLogin<MyValidator>());
// UI at  /admin/dbconfig
// API at /admin/dbconfig/api  (same cookie)
```

One signed cookie covers both surfaces. The React app calls its own backend at
`/admin/dbconfig/api/*` with no separate auth dance — `MapDbConfigAdmin` sets the cookie
`Path` to the unified prefix automatically.

### Split deployment

```csharp
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");
app.MapDbConfigHttp("/api/dbconfig");
```

The UI reads its HTTP API prefix from a `<meta name="api-prefix" content="..." />` tag
injected into `index.html` at serve time — the API prefix is never baked into the bundle.

To protect a split deployment with an authorization policy:

```csharp
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");
app.MapDbConfigHttp("/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");
```

## Embedded asset serving (v0.10.1+)

The UI's static assets (JS, CSS, favicon) are served via ASP.NET's `StaticFileMiddleware`
backed by an `EmbeddedFileProvider`. ETag, conditional `GET`, `Range` requests, and
cache headers all come for free — no hand-rolled per-file `MapGet` routes. The favicon
is embedded alongside the rest of the bundle.

## Tabbed navigation: Entries + Audit Log

The top of the UI has a two-tab header — **Entries** (the primary CRUD surface) and
**Audit Log** (the global timeline added in v0.10.1).

## Entries tab

<Screenshot light="/img/screenshots/01-entries-list.png" dark="/img/screenshots/01-entries-list-dark.png" alt="DbConfig entries list showing keys, values, scope badges, and action buttons" />

The main view shows all configuration entries on first paint — no `AppName` /
`Environment` input required. The toolbar filter fields (App, Environment, Tenant, key
prefix) narrow the table client-side via the flat `GET /` endpoint. Each row displays:

- **Key** — the configuration key, using `:` as the hierarchy separator
- **Value** — the stored value; secret entries show `••••••••` (masked) with a reveal toggle
- **Scope badge** — colored badge showing the source `AppName` (important when multiple
  apps share the same database)
- **Tenant badge** — `Default` for global entries (`TenantId = ""`); a colored chip for
  tenant-specific overrides
- **Modified by** — the identity that last wrote this entry
- **Modified at** — UTC timestamp of the last mutation
- **History button** — opens the per-entry audit history dialog
- **Edit button** — opens the wide Edit dialog (xl, 1152px) — disabled for cross-scope rows
- **Delete button** — deletes the entry with a confirmation prompt — disabled for
  cross-scope rows

### Clickable rows (v0.10.1+)

Clicking anywhere on an entries row — including plain-text cells and the Value column —
opens the Edit dialog. The checkbox, the secret-reveal eye, and the per-row action
buttons keep their own click handlers and don't trigger Edit. This dramatically reduces
target-acquisition for the common case (just click the row, don't aim for the small
Pencil icon).

### Tree-view alignment (v0.10.1+)

The tree-view mode (toggle in the toolbar) renders nested keys (`Notifications:Email:Smtp:Host`)
as a collapsible tree. Leaf checkboxes and group chevrons now share the same X
coordinate per depth — group rows use `colSpan` so the chevron lives in the same column
as a leaf's checkbox, and both indent via `paddingLeft = (depth + 1) * 28px` with subtle
vertical guide lines.

## Audit Log tab (v0.10.1+)

The Audit Log tab surfaces the full audit timeline via `GET /audit`, including Delete
events whose entries no longer exist in the Entries grid. Action chips are color-coded:
green Insert, blue Update, red Delete, grey Read. Toolbar filters (App, Environment,
Tenant, key prefix, Action) narrow the timeline; the per-row eye toggle reveals secret
old/new values inline.

See [Audit Log page](./audit-log-page.md) for the full feature reference and how it
complements the per-row History dialog.

## Softer dark mode (v0.10.0+)

The dark palette is off-black with slightly compressed contrast instead of the previous
pure-black background. The hover-on-table-row affordance also gets a softer
`mute/40 → muted` transition. Light mode is unchanged.

## Access warning banner

A persistent warning banner is always visible at the top of the UI:

> Configuration values may be visible to anyone with database access if they are not
> marked IsSecret.

This banner cannot be dismissed. It is a reminder that `IsSecret = false` entries are
stored as plaintext in the database, readable by anyone with DBA access. Mark sensitive
values `IsSecret = true` to encrypt them at rest.

## Where to go next

| Feature | Page |
|---------|------|
| Create, edit, and delete entries | [Editing entries](./editing-entries.md) |
| View per-row audit history and diffs | [History and diff](./history-and-diff.md) |
| Global audit timeline | [Audit Log page](./audit-log-page.md) |
| Bulk toggle, move, delete | [Bulk operations](./bulk-operations.md) |
| Import and export JSON | [Import and export](./import-export.md) |
| Scope selector and view modes | [Scopes in UI](./scopes-in-ui.md) |
