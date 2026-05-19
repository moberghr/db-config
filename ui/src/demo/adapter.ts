/**
 * Demo-mode replacement client for db-config UI.
 *
 * `createDemoClient()` returns an object whose methods have the exact same
 * signatures as the named exports of `@/api/entries` (queryEntries, getEntry,
 * upsertEntry, deleteEntry, triggerReload, getAuditHistory).
 *
 * All state is kept in-memory.  Writes (upsert / delete) mutate that state and
 * are visible immediately to subsequent reads within the same browser session —
 * perfect for Playwright screenshot tests and manual exploration without a
 * running backend.
 */

import type { ConfigEntry, ConfigAuditEntry, ConfigAuditAction, EntriesQuery } from '@/api/entries'
import type { AuditQuery } from '@/api/audit'
import { DEMO_ENTRIES, DEMO_AUDIT_HISTORY } from './data'

// ============================================================
// Types that mirror the entries.ts public API
// ============================================================

export interface DemoClient {
  queryEntries(q: EntriesQuery): Promise<ConfigEntry[]>
  getEntry(app: string, env: string, key: string, tenantId?: string): Promise<ConfigEntry | null>
  upsertEntry(app: string, env: string, key: string, value: string | null, isSecret: boolean, tenantId?: string): Promise<{ data: ConfigEntry; status: number; headers: Record<string, string> }>
  deleteEntry(app: string, env: string, key: string, tenantId?: string): Promise<void>
  triggerReload(): Promise<void>
  getAuditHistory(appName: string, environment: string, key: string, take?: number, tenantId?: string): Promise<ConfigAuditEntry[]>
  queryAuditEntries(q: AuditQuery): Promise<ConfigAuditEntry[]>
}

// ============================================================
// Factory
// ============================================================

export function createDemoClient(): DemoClient {
  // Deep-copy the seed data so mutations don't corrupt the module-level constants.
  const entries = new Map<string, ConfigEntry>(
    DEMO_ENTRIES.map((e) => [compositeKey(e.appName, e.environment, e.tenantId, e.key), { ...e }])
  )
  const auditHistory: ConfigAuditEntry[] = DEMO_AUDIT_HISTORY.map((a) => ({ ...a }))

  // ---- helpers ----

  function compositeKey(appName: string, environment: string, tenantId: string, key: string): string {
    return `${appName}||${environment}||${tenantId}||${key}`
  }

  function appendAudit(
    entry: ConfigEntry,
    action: ConfigAuditAction,
    oldValue: string | null,
  ): void {
    const row: ConfigAuditEntry = {
      id: `demo-live-${Date.now()}-${Math.random().toString(36).slice(2)}`,
      appName: entry.appName,
      environment: entry.environment,
      tenantId: entry.tenantId,
      key: entry.key,
      oldValue,
      newValue: entry.value,
      isSecret: entry.isSecret,
      action,
      modifiedUtc: entry.modifiedUtc,
      modifiedBy: entry.modifiedBy,
    }
    auditHistory.unshift(row)
  }

  // ---- API methods ----

  // Mirror the server's flat-query semantics: AND across filters, default take=1000,
  // case-insensitive keyPrefix, ordered by (AppName, Environment, TenantId, Key) ascending.
  async function queryEntries(q: EntriesQuery): Promise<ConfigEntry[]> {
    const take = q.take ?? 1000
    const cappedTake = Math.min(Math.max(take, 1), 10000)
    const keyPrefixLower = q.keyPrefix ? q.keyPrefix.toLowerCase() : null
    const result: ConfigEntry[] = []
    for (const entry of entries.values()) {
      if (q.appName && entry.appName.toLowerCase() !== q.appName.toLowerCase()) continue
      if (q.environment && entry.environment.toLowerCase() !== q.environment.toLowerCase()) continue
      if (q.tenantId !== undefined && entry.tenantId !== q.tenantId) continue
      if (keyPrefixLower && !entry.key.toLowerCase().startsWith(keyPrefixLower)) continue
      result.push({ ...entry })
    }
    result.sort((a, b) =>
      a.appName.localeCompare(b.appName)
      || a.environment.localeCompare(b.environment)
      || a.tenantId.localeCompare(b.tenantId)
      || a.key.localeCompare(b.key)
    )
    return result.slice(0, cappedTake)
  }

  async function getEntry(app: string, env: string, key: string, tenantId?: string): Promise<ConfigEntry | null> {
    const found = entries.get(compositeKey(app, env, tenantId ?? '', key))
    return found ? { ...found } : null
  }

  async function upsertEntry(
    app: string,
    env: string,
    key: string,
    value: string | null,
    isSecret: boolean,
    tenantId?: string,
  ): Promise<{ data: ConfigEntry; status: number; headers: Record<string, string> }> {
    const resolvedTenantId = tenantId ?? ''
    const ck = compositeKey(app, env, resolvedTenantId, key)
    const existing = entries.get(ck)
    const now = new Date().toISOString()
    const action: ConfigAuditAction = existing ? 'Update' : 'Insert'
    const oldValue = existing?.value ?? null

    const updated: ConfigEntry = {
      appName: app,
      environment: env,
      tenantId: resolvedTenantId,
      key,
      value,
      isSecret,
      modifiedUtc: now,
      modifiedBy: 'demo-user',
    }
    entries.set(ck, updated)
    appendAudit(updated, action, oldValue)

    return { data: { ...updated }, status: existing ? 200 : 201, headers: {} }
  }

  async function deleteEntry(app: string, env: string, key: string, tenantId?: string): Promise<void> {
    const resolvedTenantId = tenantId ?? ''
    const ck = compositeKey(app, env, resolvedTenantId, key)
    const existing = entries.get(ck)
    if (existing) {
      const now = new Date().toISOString()
      const deleted: ConfigEntry = { ...existing, modifiedUtc: now, modifiedBy: 'demo-user' }
      appendAudit(deleted, 'Delete', existing.value)
      entries.delete(ck)
    }
  }

  async function triggerReload(): Promise<void> {
    // No-op in demo mode — the UI refetches entries after calling this.
  }

  async function getAuditHistory(
    appName: string,
    environment: string,
    key: string,
    take: number = 50,
    tenantId?: string,
  ): Promise<ConfigAuditEntry[]> {
    const resolvedTenantId = tenantId ?? ''
    return auditHistory
      .filter((a) =>
        a.appName === appName &&
        a.environment === environment &&
        a.key === key &&
        a.tenantId === resolvedTenantId
      )
      .sort((a, b) => b.modifiedUtc.localeCompare(a.modifiedUtc))
      .slice(0, take)
      .map((a) => ({ ...a }))
  }

  // Mirror the server's flat-query semantics on the audit log: AND across
  // filters, default take=1000 (capped at 10000), case-insensitive keyPrefix,
  // ordered by ModifiedUtc DESC with Key ASC as the stable secondary sort.
  async function queryAuditEntries(q: AuditQuery): Promise<ConfigAuditEntry[]> {
    const take = q.take ?? 1000
    const cappedTake = Math.min(Math.max(take, 1), 10000)
    const keyPrefixLower = q.keyPrefix ? q.keyPrefix.toLowerCase() : null
    const result: ConfigAuditEntry[] = []
    for (const entry of auditHistory) {
      if (q.appName && entry.appName.toLowerCase() !== q.appName.toLowerCase()) continue
      if (q.environment && entry.environment.toLowerCase() !== q.environment.toLowerCase()) continue
      if (q.tenantId !== undefined && entry.tenantId !== q.tenantId) continue
      if (keyPrefixLower && !entry.key.toLowerCase().startsWith(keyPrefixLower)) continue
      if (q.action && entry.action !== q.action) continue
      result.push({ ...entry })
    }
    result.sort((a, b) => {
      if (a.modifiedUtc !== b.modifiedUtc) {
        return b.modifiedUtc.localeCompare(a.modifiedUtc)
      }
      return a.key.localeCompare(b.key)
    })
    return result.slice(0, cappedTake)
  }

  return {
    queryEntries,
    getEntry,
    upsertEntry,
    deleteEntry,
    triggerReload,
    getAuditHistory,
    queryAuditEntries,
  }
}
