---
sidebar_position: 1
---

# UI editor overview

DbConfig ships a full-featured React editor UI as embedded static assets in
`Moberg.DbConfig.Ui`. One call mounts it at any path in your ASP.NET Core application.

## Mounting the UI

```csharp
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig");
```

The first argument is the path where the SPA is served. The second is the HTTP API prefix.
The UI reads the API prefix from a `<meta name="api-prefix" content="..." />` tag injected
into `index.html` at serve time — the API prefix is never baked into the bundle.

To protect the UI with an authorization policy:

```csharp
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig")
   .RequireAuthorization("DbConfigAdmin");
```

## Entries list

#### Light:

![DbConfig entries list showing keys, values, scope badges, and action buttons](/img/screenshots/01-entries-list.png)

#### Dark:

![DbConfig entries list in dark theme](/img/screenshots/01-entries-list-dark.png)

The main view shows all configuration entries for the current scope. Each row displays:

- **Key** — the configuration key, using `:` as the hierarchy separator
- **Value** — the stored value; secret entries show `•••••` (masked) with a reveal toggle
- **Scope badge** — colored badge showing the source `AppName` (important when
  `IncludeScopes` is configured)
- **Modified by** — the identity that last wrote this entry, from
  `HttpContext.User.Identity.Name`
- **Modified at** — UTC timestamp of the last mutation
- **History button** — opens the per-entry audit history dialog
- **Edit button** — opens the edit dialog (disabled for cross-scope rows)
- **Delete button** — deletes the entry with a confirmation prompt (disabled for
  cross-scope rows)

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
| View audit history and diffs | [History and diff](./history-and-diff.md) |
| Bulk toggle, move, delete | [Bulk operations](./bulk-operations.md) |
| Import and export JSON | [Import and export](./import-export.md) |
| Scope selector and view modes | [Scopes in UI](./scopes-in-ui.md) |
