---
sidebar_position: 5
---

# Import and export

The UI provides import and export buttons in the toolbar for bulk-loading configuration
from JSON files and for saving the current scope to disk.

## Export

Click the export button to download all entries for the current scope as a JSON file.

The exported format mirrors `appsettings.json` with a metadata sidecar:

```json
{
  "Database": {
    "ConnectionString": "Server=prod-sql;..."
  },
  "Logging": {
    "Level": "Information"
  },
  "_dbconfig": {
    "Database:ConnectionString": {
      "isSecret": true,
      "modifiedUtc": "2026-05-17T12:00:00Z",
      "modifiedBy": "alice@example.com"
    },
    "Logging:Level": {
      "isSecret": false,
      "modifiedUtc": "2026-05-16T09:00:00Z",
      "modifiedBy": "bob@example.com"
    }
  }
}
```

The `_dbconfig` block at the top level is a metadata sidecar. It maps flat keys to their
`isSecret` flags, timestamps, and author information. This sidecar is required for a
lossless round-trip — without it, an import would not know which entries to encrypt.

:::note
`_dbconfig` is reserved as a top-level key only. A real configuration key named
`Section:_dbconfig` (i.e., nested under a section) would appear in the exported JSON as
`{ "Section": { "_dbconfig": "value" } }` and is imported as a regular entry without
any special treatment.
:::

## Import

#### Light:

![Import dialog showing file picker, preview table, and collision policy selector](/img/screenshots/08-import-dialog.png)

#### Dark:

![Import dialog in dark theme](/img/screenshots/08-import-dialog-dark.png)

Click the import button to open the import dialog:

1. **Pick a file** — select a `.json` file from your file system. The file must follow the
   export format above. Files without a `_dbconfig` sidecar are imported as non-secret
   entries.

2. **Preview** — the dialog shows a table of entries to be imported: key, value (masked
   for secrets), and whether the entry already exists in the current scope.

3. **Collision policy** — choose how to handle entries that already exist:
   - **Overwrite** — replaces existing values with the imported ones (default for clean
     re-imports)
   - **Skip existing** — imports only new entries; existing entries are unchanged
   - **Error on collision** — the import stops at the first existing key; partial state
     may result if some entries were imported before the collision

4. **Import** — starts looping `PUT` requests for each entry. A progress bar and per-row
   status show the result of each write.

### Collision policy note

The "Error on collision" policy does not roll back entries imported before the collision.
If you need an atomic all-or-nothing import, use the Overwrite policy for idempotence.

### IsSecret preservation

The sidecar `_dbconfig` block carries `isSecret` flags. An exported entry with
`isSecret: true` is re-imported as a secret entry (value encrypted at rest). A file
without the sidecar treats all entries as non-secret.

## Round-trip workflow

Export → edit the JSON file locally → re-import with Overwrite is a common workflow for
bulk changes:

1. Export the current scope
2. Edit values in your preferred text editor
3. Import with Overwrite to push all changes in one operation

Each `PUT` during import fires the reload signal. Consumers of the configuration provider
see updates as entries are written, not in a single atomic batch.
