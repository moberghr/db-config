import { create } from 'zustand'
import { persist } from 'zustand/middleware'

type ViewMode = 'mine' | 'shared' | 'all'
type ListMode = 'flat' | 'tree'

interface ScopeState {
  appName: string
  environment: string
  tenantId: string
  includeScopes: string[]
  viewMode: ViewMode
  listMode: ListMode
  setScope: (appName: string, environment: string) => void
  setIncludeScopes: (scopes: string[]) => void
  setTenantId: (tenantId: string) => void
  setViewMode: (mode: ViewMode) => void
  setListMode: (mode: ListMode) => void
}

export const useScopeStore = create<ScopeState>()(
  persist(
    (set) => ({
      appName: '',
      environment: '',
      tenantId: '',
      includeScopes: [],
      viewMode: 'all',
      listMode: 'flat',
      setScope: (appName, environment) => set({ appName, environment }),
      setIncludeScopes: (scopes) => set({ includeScopes: scopes }),
      setTenantId: (tenantId) => set({ tenantId }),
      setViewMode: (mode) => set({ viewMode: mode }),
      setListMode: (mode) => set({ listMode: mode }),
    }),
    {
      name: 'db-config-scope',
    }
  )
)
