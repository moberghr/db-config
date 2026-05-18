import { useState } from 'react'
import { useScopeStore } from '@/store/scopeStore'
import { useEntriesStore } from '@/store/entriesStore'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

function parseIncludeScopes(raw: string): string[] {
  return [...new Set(raw.split(',').map((s) => s.trim()).filter(Boolean))]
}

export function ScopeSelector() {
  const { appName, environment, tenantId, includeScopes, setScope, setIncludeScopes, setTenantId } = useScopeStore()
  const refresh = useEntriesStore((s) => s.refresh)

  const [localApp, setLocalApp] = useState(appName)
  const [localEnv, setLocalEnv] = useState(environment)
  const [localTenant, setLocalTenant] = useState(tenantId)
  const [localIncludeScopes, setLocalIncludeScopes] = useState(includeScopes.join(', '))

  function handleSwitch() {
    const trimmedApp = localApp.trim()
    const trimmedEnv = localEnv.trim()
    const trimmedTenant = localTenant.trim()
    const parsedScopes = parseIncludeScopes(localIncludeScopes)
    setScope(trimmedApp, trimmedEnv)
    setIncludeScopes(parsedScopes)
    setTenantId(trimmedTenant)
    void refresh({ appName: trimmedApp, environment: trimmedEnv, includeScopes: parsedScopes, tenantId: trimmedTenant })
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Enter') handleSwitch()
  }

  const isFilteringAll = !localApp.trim() && !localEnv.trim()

  return (
    <div className="flex items-center gap-2 flex-wrap">
      <label className="text-sm font-medium text-muted-foreground" htmlFor="scope-app">
        Filter App:
      </label>
      <Input
        id="scope-app"
        value={localApp}
        onChange={(e) => setLocalApp(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="(all)"
        className="w-40"
        title="Filter by AppName — leave empty to show entries from every app"
      />
      <label className="text-sm font-medium text-muted-foreground" htmlFor="scope-env">
        Env:
      </label>
      <Input
        id="scope-env"
        value={localEnv}
        onChange={(e) => setLocalEnv(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="(all)"
        className="w-40"
        title="Filter by Environment — leave empty to show entries from every environment"
      />
      <label className="text-sm font-medium text-muted-foreground" htmlFor="scope-tenant">
        Tenant:
      </label>
      <Input
        id="scope-tenant"
        value={localTenant}
        onChange={(e) => setLocalTenant(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="(global defaults)"
        className="w-40"
        title="Tenant identifier — leave empty for global defaults"
      />
      <label className="text-sm font-medium text-muted-foreground" htmlFor="scope-include">
        Include scopes:
      </label>
      <Input
        id="scope-include"
        value={localIncludeScopes}
        onChange={(e) => setLocalIncludeScopes(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="Shared, PlatformDefaults"
        className="w-52"
        title="Comma-separated list of additional scopes to include (requires App + Env)"
      />
      <Button onClick={handleSwitch} size="sm">
        Apply
      </Button>
      {isFilteringAll ? (
        <span className="text-xs text-muted-foreground italic">showing all entries</span>
      ) : null}
    </div>
  )
}
