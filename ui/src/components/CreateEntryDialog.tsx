import { useEffect, useState } from 'react'
import type { ConfigEntry } from '@/api/entries'
import { useEntriesStore } from '@/store/entriesStore'
import { useScopeStore } from '@/store/scopeStore'
import { useKnownValues } from '@/hooks/useKnownValues'
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
  /**
   * When provided, the dialog opens in "Duplicate" mode: identity fields and value/IsSecret
   * are pre-filled from this entry, the title changes to "Duplicate: {key}", and all four
   * identity fields stay editable so the user can write to a different (Scope, Environment,
   * TenantId, Key) slot. The original entry is untouched — submission is a plain PUT.
   */
  sourceEntry?: ConfigEntry | null
}

export function CreateEntryDialog({ open, onClose, sourceEntry }: CreateEntryDialogProps) {
  const upsert = useEntriesStore((s) => s.upsert)
  const scope = useScopeStore((s) => s.scope)
  const environment = useScopeStore((s) => s.environment)
  const includeScopes = useScopeStore((s) => s.includeScopes)
  const scopeTenantId = useScopeStore((s) => s.tenantId)
  const { scopes: knownScopes, environments: knownEnvironments, tenants: knownTenants } = useKnownValues()

  // In create mode (no source), show the include-scope dropdown when the user has filtered
  // by Scope. In duplicate mode, identity fields are free-text so the user can edit them.
  const filteredScopeOptions = sourceEntry == null && scope
    ? [scope, ...includeScopes.filter((s) => s !== scope)]
    : []

  const [selectedApp, setSelectedApp] = useState(sourceEntry?.scope ?? scope)
  const [selectedEnv, setSelectedEnv] = useState(sourceEntry?.environment ?? environment)
  const [tenantId, setTenantId] = useState(sourceEntry?.tenantId ?? scopeTenantId)
  const [key, setKey] = useState(sourceEntry?.key ?? '')
  const [value, setValue] = useState(sourceEntry?.value ?? '')
  const [isSecret, setIsSecret] = useState(sourceEntry?.isSecret ?? false)
  const [saving, setSaving] = useState(false)
  const [keyError, setKeyError] = useState<string | null>(null)
  const [scopeError, setScopeError] = useState<string | null>(null)
  const [apiError, setApiError] = useState<string | null>(null)

  // Re-seed the form whenever the dialog is opened with a different source entry.
  // (React keeps the same component instance across multiple open/close cycles, so
  // useState initial values only run on first mount.)
  useEffect(() => {
    if (open) {
      setSelectedApp(sourceEntry?.scope ?? scope)
      setSelectedEnv(sourceEntry?.environment ?? environment)
      setTenantId(sourceEntry?.tenantId ?? scopeTenantId)
      setKey(sourceEntry?.key ?? '')
      setValue(sourceEntry?.value ?? '')
      setIsSecret(sourceEntry?.isSecret ?? false)
      setKeyError(null)
      setScopeError(null)
      setApiError(null)
    }
  }, [open, sourceEntry, scope, environment, scopeTenantId])

  function validateKey(k: string): string | null {
    if (!k.trim()) return 'Key is required'
    if (k !== k.trim()) return 'Key must not have leading or trailing whitespace'
    return null
  }

  function validateScope(app: string, env: string): string | null {
    if (!app.trim()) return 'Scope is required'
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
    setSelectedApp(scope)
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
    // Duplicate mode: at least one identity field must differ from the source.
    // Otherwise the PUT would silently overwrite the source row (PUT is upsert) —
    // not what the user intended when they clicked Duplicate.
    if (sourceEntry != null
      && selectedApp.trim() === sourceEntry.scope
      && selectedEnv.trim() === sourceEntry.environment
      && tenantId.trim() === sourceEntry.tenantId
      && key.trim() === sourceEntry.key)
    {
      setScopeError('Change at least one of Scope, Environment, Tenant, or Key to create a new entry. The current values match the source.')
      return
    }
    setSaving(true)
    setApiError(null)
    try {
      await upsert({
        scope: selectedApp.trim(),
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

  const isDuplicate = sourceEntry != null
  const dialogTitle = isDuplicate ? `Duplicate: ${sourceEntry!.key}` : 'New Entry'

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) handleClose() }}>
      <DialogContent size="xl">
        <DialogHeader>
          <DialogTitle>{dialogTitle}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          {isDuplicate ? (
            <p className="text-xs text-muted-foreground">
              Pre-filled from the source entry. Change at least one of Scope, Environment, Tenant, or Key — otherwise the source row will be overwritten.
            </p>
          ) : null}
          {filteredScopeOptions.length > 1 ? (
            <div>
              <label className="block text-sm font-medium mb-1" htmlFor="create-scope">
                Scope
              </label>
              <select
                id="create-scope"
                value={selectedApp}
                onChange={(e) => { setSelectedApp(e.target.value); setScopeError(null) }}
                className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              >
                {filteredScopeOptions.map((s) => (
                  <option key={s} value={s}>
                    {s}{s === scope ? ' (own)' : ' (shared)'}
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <div>
              <label className="block text-sm font-medium mb-1" htmlFor="create-app">
                Scope
              </label>
              <Input
                id="create-app"
                value={selectedApp}
                onChange={(e) => { setSelectedApp(e.target.value); setScopeError(null) }}
                placeholder="e.g. PaymentsApi"
                list="create-known-scopes"
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
              list="create-known-environments"
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
              list="create-known-tenants"
            />
          </div>

          <datalist id="create-known-scopes">
            {knownScopes.map((s) => <option key={s} value={s} />)}
          </datalist>
          <datalist id="create-known-environments">
            {knownEnvironments.map((s) => <option key={s} value={s} />)}
          </datalist>
          <datalist id="create-known-tenants">
            {knownTenants.map((s) => <option key={s} value={s} />)}
          </datalist>
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
