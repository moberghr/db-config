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
  const [value, setValue] = useState(entry?.value ?? '')
  const [isSecret, setIsSecret] = useState(entry?.isSecret ?? false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Keep state in sync when entry changes
  const open = entry !== null

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
        isSecret,
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
              placeholder="(empty)"
              rows={4}
              className="min-h-[200px]"
            />
          </div>
          <Checkbox
            id="edit-secret"
            label="Is Secret"
            checked={isSecret}
            onChange={(e) => setIsSecret(e.target.checked)}
          />
          {error && <p className="text-sm text-destructive">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>
            Cancel
          </Button>
          <Button onClick={() => { void handleSave() }} disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
