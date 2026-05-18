import { useState } from 'react'
import type { ConfigEntry } from '@/api/entries'
import { useEntriesStore } from '@/store/entriesStore'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog'

interface DeleteEntryDialogProps {
  entry: ConfigEntry | null
  onClose: () => void
}

export function DeleteEntryDialog({ entry, onClose }: DeleteEntryDialogProps) {
  const remove = useEntriesStore((s) => s.remove)
  const [deleting, setDeleting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const open = entry !== null

  async function handleDelete() {
    if (!entry) return
    setDeleting(true)
    setError(null)
    try {
      await remove({
        appName: entry.appName,
        environment: entry.environment,
        tenantId: entry.tenantId,
        key: entry.key,
      })
      onClose()
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to delete'
      setError(message)
    } finally {
      setDeleting(false)
    }
  }

  const description = entry
    ? entry.tenantId
      ? `Are you sure you want to delete "${entry.key}" in ${entry.appName} / ${entry.environment} (tenant: ${entry.tenantId})? This action cannot be undone.`
      : `Are you sure you want to delete "${entry.key}" in ${entry.appName} / ${entry.environment}? This action cannot be undone.`
    : ''

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Delete Entry</DialogTitle>
          <DialogDescription>
            {description}
          </DialogDescription>
        </DialogHeader>
        {error && <p className="text-sm text-destructive mt-2">{error}</p>}
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={deleting}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={() => { void handleDelete() }} disabled={deleting}>
            {deleting ? 'Deleting…' : 'Delete'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
