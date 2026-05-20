---
sidebar_position: 3
---

import Screenshot from '@site/src/components/Screenshot';

# History and diff

Every entry has a full audit history accessible from the entries list. DbConfig records
every mutation (insert, update, delete) and can show a character-level diff between any
two consecutive values.

:::tip
The per-row history dialog covered here shows the timeline for a single key. For a
global view across all apps, environments, tenants, and keys — including Delete events
whose entries no longer exist in the Entries grid — see the
[Audit Log page](./audit-log-page.md).
:::

## Audit history dialog

Click the clock icon on any row to open the `EntryHistoryDialog`.

<Screenshot light="/img/screenshots/05-history-dialog.png" dark="/img/screenshots/05-history-dialog-dark.png" alt="Entry history dialog showing a list of audit rows with action, who, and when" />

Each row in the history dialog shows:
- **Action chip** — color-coded: Insert (green), Update (blue), Delete (red), Read (grey)
- **Modified by** — the identity that triggered the mutation
- **Modified at** — UTC timestamp
- **Old value / New value** — masked with `•••••` for secret entries; a reveal toggle
  shows the plaintext

A "Compare to previous" button appears on each Update row (and on Delete rows that have a
prior value).

## Diff view

Click "Compare to previous" to open the inline diff panel.

<Screenshot light="/img/screenshots/06-history-diff.png" dark="/img/screenshots/06-history-diff-dark.png" alt="Side-by-side diff view showing character-level changes between two values" />

The diff panel shows:
- **Left column** — OldValue
- **Right column** — NewValue
- **Character-level highlighting** — added characters in green, removed in red, unchanged
  in grey

For values that contain JSON, the diff engine parses and pretty-prints both sides before
diffing. This turns a diff of minified JSON into a readable, line-by-line comparison.

:::note
The diff is computed entirely in the browser. Values are fetched from the audit history
endpoint (already decrypted) and diffed client-side. Secret values require the reveal
toggle to be active before the diff text becomes visible.
:::

### Large value guard

Values larger than 100 KB are not diffed. Instead, the diff panel shows:

> Value too large to diff. Download the values to compare locally.

This guard prevents the browser from locking up on very large config values (for example,
embedded certificate chains or large JSON blobs).

## Secret values in history

Secret entries are masked in the history dialog with the same `•••••` treatment as the
main entries list. Each history row has its own reveal toggle, independent of other rows
and of the main list reveal state.

The audit store decrypts `OldValue` and `NewValue` server-side before returning them from
`GET /{app}/{env}/audit/{*key}`. The reveal toggle is purely a UI affordance.

## History endpoint

The history dialog calls:

```
GET {apiPrefix}/{scope}/{environment}/audit/{*key}?take=50
```

Where `{apiPrefix}` is `/admin/dbconfig/api` for unified `MapDbConfigAdmin` mounts, or
the explicit `apiPrefix` passed to `MapDbConfigUi` in split deployments. `take` defaults
to 50 and is capped at 500. Results are ordered most-recent-first. The `action` field is
serialized as its string name (`"Insert"`, `"Update"`, `"Delete"`, `"Read"`).

See [Endpoints](../http-api/endpoints.md) for the full endpoint reference.
