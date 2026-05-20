import { useMemo } from 'react'
import { useEntriesStore } from '@/store/entriesStore'

/**
 * Derives the set of distinct values currently visible in the entries store, for use as
 * autocomplete suggestions in scope/environment/tenant inputs. Cheap to recompute
 * (~hundreds of entries at most) and memoized on the entries array reference.
 *
 * Empty strings are filtered out everywhere. Tenants in particular: the global default
 * (TenantId = "") is represented as empty in the data; we don't want to surface it as a
 * suggestion since the placeholder text already covers "leave empty for global defaults".
 */
export function useKnownValues() {
  const entries = useEntriesStore((s) => s.entries)
  return useMemo(
    () => ({
      scopes: distinctSorted(entries.map((e) => e.scope)),
      environments: distinctSorted(entries.map((e) => e.environment)),
      tenants: distinctSorted(entries.map((e) => e.tenantId)),
    }),
    [entries],
  )
}

function distinctSorted(values: string[]): string[] {
  return [...new Set(values)]
    .filter((v) => v.length > 0)
    .sort((a, b) => a.localeCompare(b))
}
