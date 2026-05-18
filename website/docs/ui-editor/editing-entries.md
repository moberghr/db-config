---
sidebar_position: 2
---

# Editing entries

## Edit dialog

Click the pencil icon on any row to open the edit dialog.

#### Light:

![Edit value dialog showing the key, value field, and IsSecret toggle](/img/screenshots/02-edit-value.png)

#### Dark:

![Edit value dialog in dark theme](/img/screenshots/02-edit-value-dark.png)

The edit dialog shows:
- **Key** — read-only; keys cannot be renamed (delete and re-create instead)
- **Value** — the current value, decrypted if the entry is a secret
- **IsSecret toggle** — controls at-rest encryption and UI masking

:::warning
Flipping the `IsSecret` toggle on an entry that already has a stored value is an
unsupported edge case. Changing from `true` → `false` leaves ciphertext in a plaintext
slot; changing from `false` → `true` attempts to decrypt a plaintext value and fails. If
you need to change the secret flag, delete the entry and re-create it.
:::

Save fires a `PUT` request and the UI waits for the response before refreshing. If the
server returns `403` (for example, because the row belongs to a different scope and your
`scopeFilter` does not allow writes), the edit is rolled back and an error is shown.

## Create dialog

Click the "New entry" button in the toolbar to open the create dialog.

#### Light:

![Create entry dialog with scope picker, key field, value field, and IsSecret toggle](/img/screenshots/03-create-entry.png)

#### Dark:

![Create entry dialog in dark theme](/img/screenshots/03-create-entry-dark.png)

The create dialog includes:
- **Scope picker** — select the target `AppName` (defaults to the current scope; only
  available if `scopeFilter` is not set on the API group, which would restrict writes to
  a specific scope)
- **Key** — the configuration key; use `:` as the hierarchy separator
- **Value** — the initial value
- **IsSecret** — whether the value should be encrypted at rest

## Revealing secret values

Secret entries show `•••••` in the entries list and in dialogs. Click the eye icon to
reveal the decrypted value.

#### Light:

![Secret value revealed showing the plaintext connection string](/img/screenshots/04-secret-revealed.png)

#### Dark:

![Secret value revealed in dark theme](/img/screenshots/04-secret-revealed-dark.png)

The reveal is client-side only — the server always returns the decrypted value in the JSON
response. The `•••••` masking is a UI affordance to prevent accidental screen sharing
of sensitive values, not a security boundary.

## Cross-scope rows

When `IncludeScopes` is configured, the entries list shows rows from multiple scopes. Rows
whose `AppName` does not match the current scope have their edit and delete buttons
**disabled** with a tooltip explaining why:

> This entry belongs to scope "Shared". Switch to that scope to edit it.

This is a UX guard. Even if you attempted to write to a foreign scope via the API directly,
the server-side `scopeFilter` (if configured) would return `403`. The UI prevents the
attempt in the first place.

To edit a cross-scope row, either:
1. Use a route group configured with `scopeFilter` matching that scope (platform team's
   endpoint), or
2. Switch the scope selector to that scope (if your auth policy allows it)
