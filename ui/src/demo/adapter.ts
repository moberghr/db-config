/**
 * Demo-mode replacement client for db-config UI.
 *
 * `createDemoClient()` returns an object whose methods have the exact same
 * signatures as the named exports of `@/api/entries` (listEntries, getEntry,
 * upsertEntry, deleteEntry, triggerReload, getAuditHistory).
 *
 * All state is kept in-memory.  Writes (upsert / delete) mutate that state and
 * are visible immediately to subsequent reads within the same browser session —
 * perfect for Playwright screenshot tests and manual exploration without a
 * running backend.
 */

import type { ConfigEntry, ConfigAuditEntry, ConfigAuditAction } from '@/api/entries'
import { DEMO_ENTRIES, DEMO_AUDIT_HISTORY } from './data'

// ============================================================
// Types that mirror the entries.ts public API
// ============================================================

export interface DemoClient {
  listEntries(app: string, env: string, includeScopes?: string[], tenantId?: string, allTenants?: boolean): Promise<ConfigEntry[]>
  getEntry(app: string, env: string, key: string, tenantId?: string): Promise<ConfigEntry | null>
  upsertEntry(app: string, env: string, key: string, value: string | null, isSecret: boolean, tenantId?: string): Promise<{ data: ConfigEntry; status: number; headers: Record<string, string> }>
  deleteEntry(app: string, env: string, key: string, tenantId?: string): Promise<void>
  triggerReload(): Promise<void>
  getAuditHistory(appName: string, environment: string, key: string, take?: number, tenantId?: string): Promise<ConfigAuditEntry[]>
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

  /**
   * Mirror server behaviour: return entries whose AppName is in
   * [includeScopes..., appName] AND Environment === env, ordered by
   * scope position (includeScopes first, then own scope).
   * If tenantId is provided, only return entries matching that tenant.
   * If allTenants is true, return all entries regardless of tenant.
   * Default (no tenantId, no allTenants): return global-default (tenantId === '') entries only.
   */
  async function listEntries(app: string, env: string, includeScopes?: string[], tenantId?: string, allTenants?: boolean): Promise<ConfigEntry[]> {
    const scopeOrder: string[] = [...(includeScopes ?? []), app]
    const result: ConfigEntry[] = []
    for (const scope of scopeOrder) {
      for (const entry of entries.values()) {
        if (entry.appName === scope && entry.environment === env) {
          if (allTenants) {
            result.push({ ...entry })
          } else if (tenantId) {
            if (entry.tenantId === tenantId) {
              result.push({ ...entry })
            }
          } else {
            if (entry.tenantId === '') {
              result.push({ ...entry })
            }
          }
        }
      }
    }
    return result
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

  return { listEntries, getEntry, upsertEntry, deleteEntry, triggerReload, getAuditHistory }
}
