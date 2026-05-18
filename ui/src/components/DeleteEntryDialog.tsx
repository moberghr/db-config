import { useState } from 'react'
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
  entryKey: string | null
  entryAppName?: string | null
  onClose: () => void
}

export function DeleteEntryDialog({ entryKey, entryAppName, onClose }: DeleteEntryDialogProps) {
  const remove = useEntriesStore((s) => s.remove)
  const [deleting, setDeleting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const open = entryKey !== null

  async function handleDelete() {
    if (!entryKey) return
    setDeleting(true)
    setError(null)
    try {
      await remove(entryKey, entryAppName ?? undefined)
      onClose()
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to delete'
      setError(message)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Delete Entry</DialogTitle>
          <DialogDescription>
            Are you sure you want to delete <strong>{entryKey}</strong>?
            This action cannot be undone.
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
