import { create } from 'zustand'
import type { ConfigEntry } from '@/api/entries'
import {
  queryEntries,
  upsertEntry,
  deleteEntry,
  triggerReload,
} from '@/api/entries'
import { useScopeStore } from './scopeStore'

export interface UpsertCoords {
  scope: string
  environment: string
  tenantId: string
  key: string
  value: string | null
  isSecret: boolean
}

export interface RemoveCoords {
  scope: string
  environment: string
  tenantId: string
  key: string
}

/**
 * Default server cap on the flat-query endpoint. Mirrored client-side so the UI can detect
 * when the response was capped (entries.length === DefaultQueryTake) and surface a banner
 * suggesting filters. Keep in sync with QueryEntriesEndpoint.DefaultTake on the server.
 */
export const DefaultQueryTake = 1000

interface EntriesState {
  entries: ConfigEntry[]
  loading: boolean
  error: string | null
  truncated: boolean
  selectedKeys: Set<string>
  /**
   * Fetches entries for the current scope.
   *
   * @param overrides - "Fresh-state path": when the caller has just called
   *   `setScope(...)` / `setIncludeScopes(...)` and wants to use the new
   *   values immediately (without waiting for React reconciliation to flush
   *   the Zustand store), it can pass those values here.  When omitted,
   *   values are read from `useScopeStore.getState()` as usual.
   */
  refresh: (overrides?: { scope?: string; environment?: string; tenantId?: string }) => Promise<void>
  /**
   * Upsert an entry. Callers MUST pass the full coordinates of the target
   * entry — scope, environment, tenantId, key. The scopeStore is only a
   * UI filter and may be empty when the user is in "show all" mode.
   */
  upsert: (coords: UpsertCoords) => Promise<void>
  /**
   * Delete an entry by its full coordinates.
   */
  remove: (coords: RemoveCoords) => Promise<void>
  reload: () => Promise<void>
  toggleSelection: (compositeKey: string) => void
  selectAll: (compositeKeys: string[]) => void
  clearSelection: () => void
}

async function reloadEntriesForCurrentScope(): Promise<ConfigEntry[]> {
  const { scope, environment, tenantId } = useScopeStore.getState()
  return queryEntries({
    scope: scope || undefined,
    environment: environment || undefined,
    tenantId: tenantId || undefined,
    take: DefaultQueryTake,
  })
}

export const useEntriesStore = create<EntriesState>((set) => ({
  entries: [],
  loading: false,
  error: null,
  truncated: false,
  selectedKeys: new Set<string>(),

  refresh: async (overrides) => {
    const storeState = useScopeStore.getState()
    const scope = overrides?.scope ?? storeState.scope
    const environment = overrides?.environment ?? storeState.environment
    const tenantId = overrides?.tenantId ?? storeState.tenantId
    set({ loading: true, error: null })
    try {
      // Flat-query the unified entries endpoint with any selected filters; the toolbar
      // fields are optional filters, not required preconditions.
      const entries = await queryEntries({
        scope: scope || undefined,
        environment: environment || undefined,
        tenantId: tenantId || undefined,
        take: DefaultQueryTake,
      })
      set({ entries, loading: false, truncated: entries.length >= DefaultQueryTake })
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to load entries'
      set({ loading: false, error: message })
    }
  },

  upsert: async ({ scope, environment, tenantId, key, value, isSecret }) => {
    await upsertEntry(scope, environment, key, value, isSecret, tenantId || undefined)
    const entries = await reloadEntriesForCurrentScope()
    set({ entries, truncated: entries.length >= DefaultQueryTake })
  },

  remove: async ({ scope, environment, tenantId, key }) => {
    await deleteEntry(scope, environment, key, tenantId || undefined)
    const entries = await reloadEntriesForCurrentScope()
    set({ entries, truncated: entries.length >= DefaultQueryTake })
  },

  reload: async () => {
    await triggerReload()
    const entries = await reloadEntriesForCurrentScope()
    set({ entries, truncated: entries.length >= DefaultQueryTake })
  },

  toggleSelection: (compositeKey) => set((state) => {
    const next = new Set(state.selectedKeys)
    if (next.has(compositeKey)) {
      next.delete(compositeKey)
    } else {
      next.add(compositeKey)
    }
    return { selectedKeys: next }
  }),

  selectAll: (compositeKeys) => set({ selectedKeys: new Set(compositeKeys) }),

  clearSelection: () => set({ selectedKeys: new Set<string>() }),
}))

// Clear selection when scope changes
useScopeStore.subscribe(() => {
  useEntriesStore.getState().clearSelection()
})
