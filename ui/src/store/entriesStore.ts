import { create } from 'zustand'
import type { ConfigEntry } from '@/api/entries'
import {
  listEntries,
  queryEntries,
  upsertEntry,
  deleteEntry,
  triggerReload,
} from '@/api/entries'
import { useScopeStore } from './scopeStore'

interface EntriesState {
  entries: ConfigEntry[]
  loading: boolean
  error: string | null
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
  refresh: (overrides?: { appName?: string; environment?: string; includeScopes?: string[]; tenantId?: string }) => Promise<void>
  upsert: (key: string, value: string | null, isSecret: boolean, targetAppName?: string, tenantId?: string) => Promise<void>
  remove: (key: string, targetAppName?: string, tenantId?: string) => Promise<void>
  reload: () => Promise<void>
  toggleSelection: (compositeKey: string) => void
  selectAll: (compositeKeys: string[]) => void
  clearSelection: () => void
}

async function reloadEntriesForCurrentScope(): Promise<ConfigEntry[]> {
  const { appName, environment, includeScopes, tenantId } = useScopeStore.getState()
  if (!appName || !environment) {
    return queryEntries({
      appName: appName || undefined,
      environment: environment || undefined,
      tenantId: tenantId || undefined,
    })
  }
  return listEntries(
    appName,
    environment,
    includeScopes.length > 0 ? includeScopes : undefined,
    tenantId || undefined
  )
}

export const useEntriesStore = create<EntriesState>((set) => ({
  entries: [],
  loading: false,
  error: null,
  selectedKeys: new Set<string>(),

  refresh: async (overrides) => {
    const storeState = useScopeStore.getState()
    const appName = overrides?.appName ?? storeState.appName
    const environment = overrides?.environment ?? storeState.environment
    const includeScopes = overrides?.includeScopes ?? storeState.includeScopes
    const tenantId = overrides?.tenantId ?? storeState.tenantId
    set({ loading: true, error: null })
    try {
      let entries
      if (!appName || !environment) {
        // No scope chosen — flat query everything (server caps at 1000 rows by default).
        // The toolbar fields become optional filters instead of required preconditions.
        entries = await queryEntries({
          appName: appName || undefined,
          environment: environment || undefined,
          tenantId: tenantId || undefined,
        })
      } else {
        entries = await listEntries(
          appName,
          environment,
          includeScopes.length > 0 ? includeScopes : undefined,
          tenantId || undefined
        )
      }
      set({ entries, loading: false })
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to load entries'
      set({ loading: false, error: message })
    }
  },

  upsert: async (key, value, isSecret, targetAppName, tenantId) => {
    const { appName, environment, tenantId: scopeTenantId } = useScopeStore.getState()
    const scopeToWrite = targetAppName ?? appName
    const resolvedTenantId = tenantId ?? scopeTenantId
    await upsertEntry(scopeToWrite, environment, key, value, isSecret, resolvedTenantId || undefined)
    const entries = await reloadEntriesForCurrentScope()
    set({ entries })
  },

  remove: async (key, targetAppName, tenantId) => {
    const { appName, environment, tenantId: scopeTenantId } = useScopeStore.getState()
    const scopeToDelete = targetAppName ?? appName
    const resolvedTenantId = tenantId ?? scopeTenantId
    await deleteEntry(scopeToDelete, environment, key, resolvedTenantId || undefined)
    const entries = await reloadEntriesForCurrentScope()
    set({ entries })
  },

  reload: async () => {
    await triggerReload()
    const entries = await reloadEntriesForCurrentScope()
    set({ entries })
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
