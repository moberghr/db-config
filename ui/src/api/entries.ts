import type { AxiosResponse } from 'axios'
import api, { isDemoMode } from './client'
import type { DemoClient } from '@/demo/adapter'

export interface ConfigEntry {
  appName: string
  environment: string
  tenantId: string
  key: string
  value: string | null
  isSecret: boolean
  modifiedUtc: string
  modifiedBy: string | null
}

export type ConfigAuditAction = 'Insert' | 'Update' | 'Delete'

export interface ConfigAuditEntry {
  id: string
  appName: string
  environment: string
  tenantId: string
  key: string
  oldValue: string | null
  newValue: string | null
  isSecret: boolean
  action: ConfigAuditAction
  modifiedUtc: string // ISO 8601
  modifiedBy: string | null
}

export interface UpsertEntryRequest {
  value: string | null
  isSecret: boolean
  tenantId?: string
}

// ============================================================
// Demo-client lazy initialisation
//
// When demo mode is active the demo adapter is dynamically imported so the
// demo data lives in a separate bundle chunk.  In production (no ?demo, no
// MODE=demo) the dynamic import is never executed and the chunk is not loaded.
// ============================================================

let _demoClientPromise: Promise<DemoClient> | null = null

function getDemoClient(): Promise<DemoClient> {
  if (!_demoClientPromise) {
    _demoClientPromise = import('@/demo/adapter').then((m) => m.createDemoClient())
  }
  return _demoClientPromise
}

/** Encode a key for use in the URL path: `:` becomes `/` so the catch-all route matches. */
function encodeKey(key: string): string {
  return key.split(':').join('/')
}

// ============================================================
// Public API — same signatures as before; callers see no change
// ============================================================

export async function listEntries(app: string, env: string, includeScopes?: string[], tenantId?: string, allTenants?: boolean): Promise<ConfigEntry[]> {
  if (isDemoMode) {
    const client = await getDemoClient()
    return client.listEntries(app, env, includeScopes, tenantId, allTenants)
  }
  const params: Record<string, string> = {}
  if (includeScopes && includeScopes.length > 0) {
    params.includeScopes = includeScopes.join(',')
  }
  if (tenantId) {
    params.tenantId = tenantId
  }
  if (allTenants) {
    params.allTenants = 'true'
  }
  const response = await api.get<ConfigEntry[]>(`/${encodeURIComponent(app)}/${encodeURIComponent(env)}`, { params })
  return response.data
}

export async function getEntry(app: string, env: string, key: string, tenantId?: string, fallback?: boolean): Promise<ConfigEntry> {
  if (isDemoMode) {
    const client = await getDemoClient()
    const entry = await client.getEntry(app, env, key, tenantId)
    if (!entry) throw new Error(`Entry not found: ${key}`)
    return entry
  }
  const params: Record<string, string> = {}
  if (tenantId) {
    params.tenantId = tenantId
  }
  if (fallback) {
    params.fallback = 'true'
  }
  const response = await api.get<ConfigEntry>(
    `/${encodeURIComponent(app)}/${encodeURIComponent(env)}/${encodeKey(key)}`,
    { params }
  )
  return response.data
}

export async function upsertEntry(
  app: string,
  env: string,
  key: string,
  value: string | null,
  isSecret: boolean,
  tenantId?: string
): Promise<AxiosResponse<ConfigEntry>> {
  if (isDemoMode) {
    const client = await getDemoClient()
    const result = await client.upsertEntry(app, env, key, value, isSecret, tenantId)
    // Return an Axios-shaped response so callers that check .data or .status work.
    return result as AxiosResponse<ConfigEntry>
  }
  const body: UpsertEntryRequest = { value, isSecret, ...(tenantId ? { tenantId } : {}) }
  return api.put<ConfigEntry>(
    `/${encodeURIComponent(app)}/${encodeURIComponent(env)}/${encodeKey(key)}`,
    body
  )
}

export async function deleteEntry(app: string, env: string, key: string, tenantId?: string): Promise<void> {
  if (isDemoMode) {
    const client = await getDemoClient()
    return client.deleteEntry(app, env, key, tenantId)
  }
  const params: Record<string, string> = {}
  if (tenantId) {
    params.tenantId = tenantId
  }
  await api.delete(`/${encodeURIComponent(app)}/${encodeURIComponent(env)}/${encodeKey(key)}`, { params })
}

export async function triggerReload(): Promise<void> {
  if (isDemoMode) {
    const client = await getDemoClient()
    return client.triggerReload()
  }
  await api.post('/reload')
}

export async function getAuditHistory(
  appName: string,
  environment: string,
  key: string,
  take: number = 50,
  tenantId?: string
): Promise<ConfigAuditEntry[]> {
  if (isDemoMode) {
    const client = await getDemoClient()
    return client.getAuditHistory(appName, environment, key, take, tenantId)
  }
  const params: Record<string, string | number> = { take }
  if (tenantId) {
    params.tenantId = tenantId
  }
  const response = await api.get<ConfigAuditEntry[]>(
    `/audit/${encodeURIComponent(appName)}/${encodeURIComponent(environment)}/${encodeKey(key)}`,
    { params }
  )
  return response.data
}
