/**
 * Helpers for converting between the flat "Section:Sub:Key" format used by DbConfig
 * and the nested JSON shape used by appsettings.json.
 *
 * Array handling: JSON arrays encountered during import are stringified and stored as a
 * single flat value (not exploded into `[0]`, `[1]` keys) because ASP.NET Configuration
 * array syntax is ambiguous and not round-trippable through the standard
 * IConfiguration flat model.
 *
 * Sidecar key: the top-level `_dbconfig` key in an imported JSON is reserved for the
 * metadata sidecar (IsSecret flags + provenance). Nested keys like `SomeSection:_dbconfig`
 * are NOT reserved — they are imported as regular config entries. Avoid naming top-level
 * keys `_dbconfig` in your config.
 */

import type { ConfigEntry } from '@/api/entries'

/** Shape of the _dbconfig metadata sidecar embedded in the exported JSON. */
interface DbConfigSidecar {
  entries: Array<{
    key: string
    isSecret: boolean
    modifiedUtc: string
    modifiedBy: string | null
  }>
}

/** Result of flatToNested. */
export interface FlatToNestedResult {
  /** The nested configuration object (appsettings shape). */
  config: Record<string, unknown>
  /** The _dbconfig metadata sidecar. */
  metadata: { _dbconfig: DbConfigSidecar }
}

/**
 * Convert an array of flat ConfigEntry records into a nested appsettings.json-shaped
 * object plus a metadata sidecar.
 *
 * - Entries with `value === null` are skipped.
 * - Keys are split on `:` to build the nested structure. A key like `Logging:LogLevel:Default`
 *   becomes `{ Logging: { LogLevel: { Default: value } } }`.
 * - The returned `metadata._dbconfig.entries` contains provenance (isSecret, modifiedUtc,
 *   modifiedBy) for each exported entry, enabling a lossless round-trip.
 */
export function flatToNested(entries: ConfigEntry[]): FlatToNestedResult {
  const config: Record<string, unknown> = {}
  const sidecarEntries: DbConfigSidecar['entries'] = []

  for (const entry of entries) {
    if (entry.value === null) continue

    // Build nested object
    const parts = entry.key.split(':')
    let node: Record<string, unknown> = config
    for (let i = 0; i < parts.length - 1; i++) {
      const part = parts[i]
      if (!(part in node) || typeof node[part] !== 'object' || node[part] === null) {
        node[part] = {}
      }
      node = node[part] as Record<string, unknown>
    }
    node[parts[parts.length - 1]] = entry.value

    // Record metadata
    sidecarEntries.push({
      key: entry.key,
      isSecret: entry.isSecret,
      modifiedUtc: entry.modifiedUtc,
      modifiedBy: entry.modifiedBy,
    })
  }

  return {
    config,
    metadata: {
      _dbconfig: {
        entries: sidecarEntries,
      },
    },
  }
}

/** A flat entry ready for import. */
export interface FlatImportEntry {
  key: string
  value: string
  isSecret: boolean
}

/**
 * Walk a nested JSON object and emit flat `Section:Sub:Key` = value pairs.
 *
 * - Leaf values (string/number/boolean) are converted to string.
 * - `null` leaves are skipped (treated as "no value").
 * - Arrays are JSON-stringified as a single leaf value (not exploded into `[0]`, `[1]`
 *   keys) — see module-level doc for rationale.
 * - The `_dbconfig` top-level key is treated as the metadata sidecar and is not emitted
 *   as a regular entry.
 * - If a `_dbconfig.entries` sidecar is present, `isSecret` is looked up by key;
 *   otherwise `isSecret` defaults to `false`.
 */
export function nestedToFlat(json: unknown): FlatImportEntry[] {
  if (typeof json !== 'object' || json === null || Array.isArray(json)) {
    return []
  }

  const root = json as Record<string, unknown>

  // Extract sidecar for isSecret lookup
  const secretMap = new Map<string, boolean>()
  const sidecar = root['_dbconfig']
  if (
    typeof sidecar === 'object' &&
    sidecar !== null &&
    !Array.isArray(sidecar) &&
    'entries' in sidecar &&
    Array.isArray((sidecar as Record<string, unknown>)['entries'])
  ) {
    const sidecarEntries = (sidecar as DbConfigSidecar).entries
    for (const e of sidecarEntries) {
      if (typeof e.key === 'string') {
        secretMap.set(e.key, !!e.isSecret)
      }
    }
  }

  const results: FlatImportEntry[] = []

  function walk(node: unknown, prefix: string): void {
    if (node === null || node === undefined) {
      return
    }
    if (Array.isArray(node)) {
      // Stringify arrays as a single value — see module-level doc
      results.push({
        key: prefix,
        value: JSON.stringify(node),
        isSecret: secretMap.get(prefix) ?? false,
      })
      return
    }
    if (typeof node === 'object') {
      const obj = node as Record<string, unknown>
      for (const [k, v] of Object.entries(obj)) {
        const childKey = prefix ? `${prefix}:${k}` : k
        walk(v, childKey)
      }
      return
    }
    // Primitive leaf
    results.push({
      key: prefix,
      value: String(node),
      isSecret: secretMap.get(prefix) ?? false,
    })
  }

  for (const [k, v] of Object.entries(root)) {
    // Skip the metadata sidecar
    if (k === '_dbconfig') continue
    walk(v, k)
  }

  return results
}
