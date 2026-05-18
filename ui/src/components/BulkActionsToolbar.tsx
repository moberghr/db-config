import { useState } from 'react'
import { useEntriesStore } from '@/store/entriesStore'
import { useScopeStore } from '@/store/scopeStore'
import { upsertEntry, deleteEntry } from '@/api/entries'
import type { ConfigEntry } from '@/api/entries'
import { Button } from '@/components/ui/button'
import { BulkOperationDialog } from './BulkOperationDialog'
import { X, ShieldOff, MoveRight, Trash2 } from 'lucide-react'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog'

type BulkAction = 'toggleSecret' | 'move' | 'delete' | null

interface BulkActionsToolbarProps {
  visibleEntries: ConfigEntry[]
}

export function BulkActionsToolbar({ visibleEntries }: BulkActionsToolbarProps) {
  const selectedKeys = useEntriesStore((s) => s.selectedKeys)
  const clearSelection = useEntriesStore((s) => s.clearSelection)
  const refresh = useEntriesStore((s) => s.refresh)
  const currentAppName = useScopeStore((s) => s.appName)
  const environment = useScopeStore((s) => s.environment)
  const includeScopes = useScopeStore((s) => s.includeScopes)

  const [activeAction, setActiveAction] = useState<BulkAction>(null)
  const [movePickerOpen, setMovePickerOpen] = useState(false)
  const [targetScope, setTargetScope] = useState(currentAppName)

  if (selectedKeys.size === 0) return null

  // Resolve selected entries from visibleEntries
  function getSelectedEntries(): ConfigEntry[] {
    return visibleEntries.filter((e) => {
      const ck = `${e.appName}|${e.environment}|${e.key}`
      return selectedKeys.has(ck)
    })
  }

  const selectedEntries = getSelectedEntries()

  // Scope options for move picker
  const scopeOptions = [currentAppName, ...includeScopes.filter((s) => s !== currentAppName)]

  // Toggle IsSecret
  async function executeToggleSecret(entry: ConfigEntry): Promise<void> {
    await upsertEntry(entry.appName, entry.environment, entry.key, entry.value, !entry.isSecret)
  }

  // Move: PUT to new scope, then DELETE from old scope only when PUT succeeded.
  // Axios throws on non-2xx by default, but we add an explicit status check as a
  // belt-and-suspenders guard against proxies that swallow error status codes.
  async function executeMove(entry: ConfigEntry): Promise<void> {
    const putResponse = await upsertEntry(targetScope, environment, entry.key, entry.value, entry.isSecret)
    if (putResponse.status < 200 || putResponse.status >= 300) {
      throw new Error(`Move failed: PUT returned status ${putResponse.status}`)
    }
    if (entry.appName !== targetScope) {
      await deleteEntry(entry.appName, entry.environment, entry.key)
    }
  }

  // Delete
  async function executeDelete(entry: ConfigEntry): Promise<void> {
    await deleteEntry(entry.appName, entry.environment, entry.key)
  }

  function afterBulkClose() {
    setActiveAction(null)
    clearSelection()
    void refresh()
  }

  function handleMoveConfirm() {
    setMovePickerOpen(false)
    setActiveAction('move')
  }

  function getEntryLabel(entry: ConfigEntry): string {
    return `${entry.appName} / ${entry.key}`
  }

  return (
    <>
      <div className="flex items-center gap-2 rounded-md border border-border bg-muted/40 px-4 py-2">
        <span className="text-sm font-medium">{selectedKeys.size} selected</span>
        <Button
          variant="ghost"
          size="icon"
          className="h-6 w-6"
          title="Clear selection"
          onClick={clearSelection}
        >
          <X className="h-3.5 w-3.5" />
        </Button>
        <div className="ml-auto flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            onClick={() => setActiveAction('toggleSecret')}
          >
            <ShieldOff className="h-3.5 w-3.5" />
            Toggle IsSecret
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            onClick={() => setMovePickerOpen(true)}
          >
            <MoveRight className="h-3.5 w-3.5" />
            Move to scope
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5 text-destructive hover:text-destructive"
            onClick={() => setActiveAction('delete')}
          >
            <Trash2 className="h-3.5 w-3.5" />
            Delete selected
          </Button>
        </div>
      </div>

      {/* Move-to-scope scope picker dialog */}
      <Dialog open={movePickerOpen} onOpenChange={(o) => { if (!o) setMovePickerOpen(false) }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Move to scope</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            <p className="text-sm text-muted-foreground">
              Select the target scope for the {selectedKeys.size} selected{' '}
              {selectedKeys.size === 1 ? 'entry' : 'entries'}.
            </p>
            <div>
              <label className="block text-sm font-medium mb-1" htmlFor="bulk-move-scope">
                Target scope
              </label>
              <select
                id="bulk-move-scope"
                value={targetScope}
                onChange={(e) => setTargetScope(e.target.value)}
                className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              >
                {scopeOptions.map((scope) => (
                  <option key={scope} value={scope}>
                    {scope}{scope === currentAppName ? ' (own)' : ' (shared)'}
                  </option>
                ))}
              </select>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setMovePickerOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleMoveConfirm}>
              Move
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Toggle IsSecret dialog */}
      {activeAction === 'toggleSecret' && (
        <BulkOperationDialog
          open
          onClose={afterBulkClose}
          title="Toggle IsSecret"
          message={`Toggle the IsSecret flag on ${selectedEntries.length} selected ${selectedEntries.length === 1 ? 'entry' : 'entries'}? Entries that are secrets will become plain-text and vice versa.`}
          items={selectedEntries}
          getLabel={getEntryLabel}
          executeOne={executeToggleSecret}
        />
      )}

      {/* Move to scope dialog */}
      {activeAction === 'move' && (
        <BulkOperationDialog
          open
          onClose={afterBulkClose}
          title="Move to scope"
          message={`Move ${selectedEntries.length} selected ${selectedEntries.length === 1 ? 'entry' : 'entries'} to scope "${targetScope}"?`}
          items={selectedEntries}
          getLabel={getEntryLabel}
          executeOne={executeMove}
        />
      )}

      {/* Delete dialog */}
      {activeAction === 'delete' && (
        <BulkOperationDialog
          open
          onClose={afterBulkClose}
          title="Delete selected"
          message={`Permanently delete ${selectedEntries.length} selected ${selectedEntries.length === 1 ? 'entry' : 'entries'}? This cannot be undone.`}
          items={selectedEntries}
          getLabel={getEntryLabel}
          executeOne={executeDelete}
        />
      )}
    </>
  )
}
