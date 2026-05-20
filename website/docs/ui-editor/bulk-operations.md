---
sidebar_position: 4
---

import Screenshot from '@site/src/components/Screenshot';

# Bulk operations

The UI supports selecting multiple rows and performing batch actions on them — toggling
the secret flag, moving entries to a different scope, or deleting a set of entries.

## Selecting rows

Each row in the entries list has a checkbox in the leftmost column. The header row has a
"select all" checkbox that selects or deselects all visible rows.

When one or more rows are selected, the `BulkActionsToolbar` appears above the table.

<Screenshot light="/img/screenshots/07-bulk-edit-toolbar.png" dark="/img/screenshots/07-bulk-edit-toolbar-dark.png" alt="Bulk actions toolbar showing Toggle IsSecret, Move to scope, and Delete selected buttons with 3 rows selected" />

Cross-scope rows (from included scopes) can be selected. This is intentional for the
"Move to scope" operation, which reads from one scope and writes to another.

## Available bulk actions

### Toggle IsSecret

Flips the `IsSecret` flag on all selected entries. If a mix of secret and non-secret
entries is selected, a confirmation dialog explains the implications:

- Entries becoming `IsSecret = true` will have their values encrypted at rest
- Entries becoming `IsSecret = false` will store values as plaintext

:::warning
Toggling `IsSecret` on entries that already have stored values is an unsupported edge case
(see [Editing entries](./editing-entries.md#edit-dialog)). The bulk toggle sets the flag
on a fresh `PUT` with the existing value. If the flag and the stored value are mismatched
(one encrypted, one not), decryption will fail on next read. Use this operation only on
entries where you understand the current storage state.
:::

### Move to scope

Moves selected entries to a different scope (different `Scope`). A scope picker dialog
appears; choose the target scope from a dropdown.

The move operation runs per entry:
1. `PUT` to `/{targetScope}/{env}/{key}` with the current value and `isSecret` flag
2. If the PUT succeeds: `DELETE` from `/{sourceScope}/{env}/{key}`
3. If the PUT fails: the DELETE is skipped — the original entry is preserved

This two-step approach avoids data loss if the write to the target scope fails. It is
TOCTOU-aware: if two users try to move the same entry simultaneously, one will succeed and
the other's PUT will overwrite the first, then delete the source. The net result is one
entry in the target scope.

### Delete selected

Shows a confirmation dialog listing the selected keys, then loops `DELETE` requests for
each entry. A per-row progress dialog shows success or failure for each deletion.

## Per-item progress

All bulk operations show a progress dialog with a row for each selected entry. Each row
shows:

- Entry key
- Status: pending / in progress / success / failed
- Error message on failure (e.g. `403 Forbidden` if the entry is in a cross-scope that
  your auth policy does not allow writes to)

The operation continues past individual failures. The summary shows total succeeded and
total failed counts.

## Implementation note

Bulk operations use existing `PUT` and `DELETE` endpoints in a client-side loop. There is
no server-side bulk endpoint. For an editor-scale workload (humans selecting 5–50 entries),
the sequential HTTP calls complete in well under a second. Each successful mutation also
fires the in-process reload signal, so configuration consumers see updates as each entry
is processed.
