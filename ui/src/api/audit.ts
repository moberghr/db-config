import api, { isDemoMode } from './client'
import type { ConfigAuditAction, ConfigAuditEntry } from './entries'
import type { DemoClient } from '@/demo/adapter'

export interface AuditQuery {
  scope?: string
  environment?: string
  tenantId?: string
  keyPrefix?: string
  action?: ConfigAuditAction
  take?: number
}

// Mirror the lazy-loaded demo client pattern from `@/api/entries`.
let _demoClientPromise: Promise<DemoClient> | null = null

function getDemoClient(): Promise<DemoClient> {
  if (!_demoClientPromise) {
    _demoClientPromise = import('@/demo/adapter').then((m) => m.createDemoClient())
  }
  return _demoClientPromise
}

/**
 * Flat-query the audit log with optional filters. Used by the global Audit Log
 * page to surface audit rows across all keys — including audit history for
 * entries that no longer exist (e.g. deleted entries' Insert + Delete trail).
 *
 * Each non-empty field of `q` narrows the result server-side (AND semantics).
 * Empty `q` returns everything (capped by the server's default take of 1000,
 * max 10000).
 */
export async function queryAuditEntries(q: AuditQuery = {}): Promise<ConfigAuditEntry[]> {
  if (isDemoMode) {
    const client = await getDemoClient()
    return client.queryAuditEntries(q)
  }
  const params = new URLSearchParams()
  if (q.scope) params.set('scope', q.scope)
  if (q.environment) params.set('environment', q.environment)
  if (q.tenantId) params.set('tenantId', q.tenantId)
  if (q.keyPrefix) params.set('keyPrefix', q.keyPrefix)
  if (q.action) params.set('action', q.action)
  if (q.take) params.set('take', String(q.take))
  const qs = params.toString()
  const response = await api.get<ConfigAuditEntry[]>(qs ? `/audit?${qs}` : '/audit')
  return response.data
}
