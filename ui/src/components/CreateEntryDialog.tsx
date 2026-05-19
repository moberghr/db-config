import { useState } from 'react'
import { useEntriesStore } from '@/store/entriesStore'
import { useScopeStore } from '@/store/scopeStore'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog'

interface CreateEntryDialogProps {
  open: boolean
  onClose: () => void
}

export function CreateEntryDialog({ open, onClose }: CreateEntryDialogProps) {
  const upsert = useEntriesStore((s) => s.upsert)
  const appName = useScopeStore((s) => s.appName)
  const environment = useScopeStore((s) => s.environment)
  const includeScopes = useScopeStore((s) => s.includeScopes)
  const scopeTenantId = useScopeStore((s) => s.tenantId)

  // When the user has set an AppName filter, expose include-scope options too.
  // When the filter is empty (multi-scope "show all" mode), the user must type
  // the target AppName + Environment explicitly.
  const filteredScopeOptions = appName
    ? [appName, ...includeScopes.filter((s) => s !== appName)]
    : []

  const [selectedApp, setSelectedApp] = useState(appName)
  const [selectedEnv, setSelectedEnv] = useState(environment)
  const [tenantId, setTenantId] = useState(scopeTenantId)
  const [key, setKey] = useState('')
  const [value, setValue] = useState('')
  const [isSecret, setIsSecret] = useState(false)
  const [saving, setSaving] = useState(false)
  const [keyError, setKeyError] = useState<string | null>(null)
  const [scopeError, setScopeError] = useState<string | null>(null)
  const [apiError, setApiError] = useState<string | null>(null)

  function validateKey(k: string): string | null {
    if (!k.trim()) return 'Key is required'
    if (k !== k.trim()) return 'Key must not have leading or trailing whitespace'
    return null
  }

  function validateScope(app: string, env: string): string | null {
    if (!app.trim()) return 'AppName is required'
    if (!env.trim()) return 'Environment is required'
    return null
  }

  function handleClose() {
    setKey('')
    setValue('')
    setIsSecret(false)
    setKeyError(null)
    setScopeError(null)
    setApiError(null)
    setSelectedApp(appName)
    setSelectedEnv(environment)
    setTenantId(scopeTenantId)
    onClose()
  }

  async function handleCreate() {
    const keyErr = validateKey(key)
    if (keyErr) {
      setKeyError(keyErr)
      return
    }
    const scopeErr = validateScope(selectedApp, selectedEnv)
    if (scopeErr) {
      setScopeError(scopeErr)
      return
    }
    setSaving(true)
    setApiError(null)
    try {
      await upsert({
        appName: selectedApp.trim(),
        environment: selectedEnv.trim(),
        tenantId: tenantId.trim(),
        key: key.trim(),
        value: value || null,
        isSecret,
      })
      handleClose()
    } catch (e: unknown) {
      let message = e instanceof Error ? e.message : 'Failed to create entry'
      // Surface 403 cross-scope write rejection clearly
      if (typeof e === 'object' && e !== null && 'response' in e) {
        const resp = (e as { response?: { status?: number } }).response
        if (resp?.status === 403) {
          message = 'This scope is read-only from this UI (403 Forbidden). Switch to a host with write access for that scope.'
        }
      }
      setApiError(message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) handleClose() }}>
      <DialogContent size="xl">
        <DialogHeader>
          <DialogTitle>New Entry</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          {filteredScopeOptions.length > 1 ? (
            <div>
              <label className="block text-sm font-medium mb-1" htmlFor="create-scope">
                Scope (AppName)
              </label>
              <select
                id="create-scope"
                value={selectedApp}
                onChange={(e) => { setSelectedApp(e.target.value); setScopeError(null) }}
                className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              >
                {filteredScopeOptions.map((scope) => (
                  <option key={scope} value={scope}>
                    {scope}{scope === appName ? ' (own)' : ' (shared)'}
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <div>
              <label className="block text-sm font-medium mb-1" htmlFor="create-app">
                AppName
              </label>
              <Input
                id="create-app"
                value={selectedApp}
                onChange={(e) => { setSelectedApp(e.target.value); setScopeError(null) }}
                placeholder="e.g. PaymentsApi"
              />
            </div>
          )}
          <div>
            <label className="block text-sm font-medium mb-1" htmlFor="create-env">
              Environment
            </label>
            <Input
              id="create-env"
              value={selectedEnv}
              onChange={(e) => { setSelectedEnv(e.target.value); setScopeError(null) }}
              placeholder="e.g. Production"
            />
          </div>
          {scopeError && <p className="text-xs text-destructive">{scopeError}</p>}
          <div>
            <label className="block text-sm font-medium mb-1" htmlFor="create-tenant">
              Tenant (leave empty for global default)
            </label>
            <Input
              id="create-tenant"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              placeholder="e.g. Acme"
            />
          </div>
          <div>
            <label className="block text-sm font-medium mb-1" htmlFor="create-key">
              Key
            </label>
            <Input
              id="create-key"
              value={key}
              onChange={(e) => { setKey(e.target.value); setKeyError(null) }}
              placeholder="MySection:MyKey"
            />
            {keyError && <p className="text-xs text-destructive mt-1">{keyError}</p>}
          </div>
          <div>
            <label className="block text-sm font-medium mb-1" htmlFor="create-value">
              Value
            </label>
            <Textarea
              id="create-value"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder="(empty)"
              rows={4}
              className="min-h-[200px]"
            />
          </div>
          <Checkbox
            id="create-secret"
            label="Is Secret"
            checked={isSecret}
            onChange={(e) => setIsSecret(e.target.checked)}
          />
          {apiError && <p className="text-sm text-destructive">{apiError}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>
            Cancel
          </Button>
          <Button onClick={() => { void handleCreate() }} disabled={saving}>
            {saving ? 'Creating…' : 'Create'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
