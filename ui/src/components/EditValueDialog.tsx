import { useState } from 'react'
import type { ConfigEntry } from '@/api/entries'
import { useEntriesStore } from '@/store/entriesStore'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog'

interface EditValueDialogProps {
  entry: ConfigEntry | null
  onClose: () => void
}

export function EditValueDialog({ entry, onClose }: EditValueDialogProps) {
  const upsert = useEntriesStore((s) => s.upsert)
  // A write-only entry's value is never sent to the browser, so editing always starts empty
  // and saving replaces the stored value.
  const [value, setValue] = useState(entry?.isWriteOnly ? '' : (entry?.value ?? ''))
  const [isSecret, setIsSecret] = useState(entry?.isSecret ?? false)
  const [isWriteOnly, setIsWriteOnly] = useState(entry?.isWriteOnly ?? false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // The dialog stays mounted across edits, so useState initial values only run once. Re-seed
  // (during render, per React's "adjusting state when a prop changes") whenever a different
  // entry opens; otherwise Save would clear IsSecret / IsWriteOnly.
  const [seededFor, setSeededFor] = useState(entry)
  if (entry !== seededFor) {
    setSeededFor(entry)
    if (entry) {
      setValue(entry.isWriteOnly ? '' : (entry.value ?? ''))
      setIsSecret(entry.isSecret)
      setIsWriteOnly(entry.isWriteOnly)
      setError(null)
    }
  }

  const open = entry !== null
  // A write-only value can never be read back, so saving one empty would silently wipe it —
  // including when the flag is being cleared, since the field never held the current value.
  const missingWriteOnlyValue = (isWriteOnly || (entry?.isWriteOnly ?? false)) && value === ''

  function handleWriteOnlyChange(checked: boolean) {
    setIsWriteOnly(checked)
    // Upgrading means rotating: the current value has already been visible, so ask for a new one.
    if (checked && !entry?.isWriteOnly) {
      setValue('')
    }
  }

  async function handleSave() {
    if (!entry) return
    setSaving(true)
    setError(null)
    try {
      await upsert({
        scope: entry.scope,
        environment: entry.environment,
        tenantId: entry.tenantId,
        key: entry.key,
        value: value || null,
        isSecret: isSecret || isWriteOnly,
        isWriteOnly,
      })
      onClose()
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to save'
      setError(message)
    } finally {
      setSaving(false)
    }
  }

  const dialogTitle = entry
    ? entry.tenantId
      ? `Edit: ${entry.key} (tenant: ${entry.tenantId})`
      : `Edit: ${entry.key}`
    : 'Edit'

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent size="xl">
        <DialogHeader>
          <DialogTitle>{dialogTitle}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium mb-1" htmlFor="edit-value">
              Value
            </label>
            <Textarea
              id="edit-value"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder={entry?.isWriteOnly ? 'Enter a new value; the current value cannot be shown' : '(empty)'}
              rows={4}
              className="min-h-[200px]"
            />
            {entry?.isWriteOnly && (
              <p className="mt-1 text-xs text-muted-foreground">
                This entry is write-only. Saving replaces the stored value.
              </p>
            )}
          </div>
          <Checkbox
            id="edit-secret"
            label="Is Secret"
            checked={isSecret || isWriteOnly}
            disabled={isWriteOnly}
            onChange={(e) => setIsSecret(e.target.checked)}
          />
          <Checkbox
            id="edit-write-only"
            label="Write-only (value can never be shown again)"
            checked={isWriteOnly}
            onChange={(e) => handleWriteOnlyChange(e.target.checked)}
          />
          {missingWriteOnlyValue && (
            <p className="text-xs text-muted-foreground">
              {entry?.isWriteOnly
                ? 'Enter a value to save. Saving replaces the stored secret.'
                : 'Enter a new value to make this entry write-only. Rotate the secret: the previous value has already been visible.'}
            </p>
          )}
          {error && <p className="text-sm text-destructive">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>
            Cancel
          </Button>
          <Button onClick={() => { void handleSave() }} disabled={saving || missingWriteOnlyValue}>
            {saving ? 'Saving…' : 'Save'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
