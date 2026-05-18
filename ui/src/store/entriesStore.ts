import { create } from 'zustand'
import type { ConfigEntry } from '@/api/entries'
import {
  listEntries,
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
    if (!appName || !environment) {
      set({ entries: [], error: null })
      return
    }
    set({ loading: true, error: null })
    try {
      const entries = await listEntries(
        appName,
        environment,
        includeScopes.length > 0 ? includeScopes : undefined,
        tenantId || undefined
      )
      set({ entries, loading: false })
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to load entries'
      set({ loading: false, error: message })
    }
  },

  upsert: async (key, value, isSecret, targetAppName, tenantId) => {
    const { appName, environment, includeScopes, tenantId: scopeTenantId } = useScopeStore.getState()
    const scopeToWrite = targetAppName ?? appName
    const resolvedTenantId = tenantId ?? scopeTenantId
    await upsertEntry(scopeToWrite, environment, key, value, isSecret, resolvedTenantId || undefined)
    const entries = await listEntries(
      appName,
      environment,
      includeScopes.length > 0 ? includeScopes : undefined,
      scopeTenantId || undefined
    )
    set({ entries })
  },

  remove: async (key, targetAppName, tenantId) => {
    const { appName, environment, includeScopes, tenantId: scopeTenantId } = useScopeStore.getState()
    const scopeToDelete = targetAppName ?? appName
    const resolvedTenantId = tenantId ?? scopeTenantId
    await deleteEntry(scopeToDelete, environment, key, resolvedTenantId || undefined)
    const entries = await listEntries(
      appName,
      environment,
      includeScopes.length > 0 ? includeScopes : undefined,
      scopeTenantId || undefined
    )
    set({ entries })
  },

  reload: async () => {
    const { appName, environment, includeScopes, tenantId } = useScopeStore.getState()
    await triggerReload()
    if (appName && environment) {
      const entries = await listEntries(
        appName,
        environment,
        includeScopes.length > 0 ? includeScopes : undefined,
        tenantId || undefined
      )
      set({ entries })
    }
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
