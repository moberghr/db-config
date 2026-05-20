import { useEffect, useState } from 'react'
import type { ConfigEntry } from '@/api/entries'
import { useEntriesStore } from '@/store/entriesStore'
import { useScopeStore } from '@/store/scopeStore'
import { AccessWarningBanner } from '@/components/AccessWarningBanner'
import { ScopeSelector } from '@/components/ScopeSelector'
import { ReloadButton } from '@/components/ReloadButton'
import { EntriesTable } from '@/components/EntriesTable'
import { EntriesTreeView } from '@/components/EntriesTreeView'
import { ViewModeToggle } from '@/components/ViewModeToggle'
import { ListModeToggle } from '@/components/ListModeToggle'
import { EditValueDialog } from '@/components/EditValueDialog'
import { CreateEntryDialog } from '@/components/CreateEntryDialog'
import { DeleteEntryDialog } from '@/components/DeleteEntryDialog'
import { EntryHistoryDialog } from '@/components/EntryHistoryDialog'
import { BulkActionsToolbar } from '@/components/BulkActionsToolbar'
import { TruncationBanner } from '@/components/TruncationBanner'
import { ExportButton } from '@/components/ExportButton'
import { ImportDialog } from '@/components/ImportDialog'
import { ThemeToggle } from '@/components/ThemeToggle'
import { Button } from '@/components/ui/button'
import { Plus, Upload } from 'lucide-react'

interface EntriesPageProps {
  header?: React.ReactNode
  headerExtras?: React.ReactNode
}

export function EntriesPage({ header, headerExtras }: EntriesPageProps = {}) {
  const refresh = useEntriesStore((s) => s.refresh)
  const entries = useEntriesStore((s) => s.entries)
  const scope = useScopeStore((s) => s.scope)
  const viewMode = useScopeStore((s) => s.viewMode)
  const listMode = useScopeStore((s) => s.listMode)

  const [editingEntry, setEditingEntry] = useState<ConfigEntry | null>(null)
  const [deletingEntry, setDeletingEntry] = useState<ConfigEntry | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [duplicateSource, setDuplicateSource] = useState<ConfigEntry | null>(null)
  const [historyEntry, setHistoryEntry] = useState<ConfigEntry | null>(null)
  const [importOpen, setImportOpen] = useState(false)

  function handleDuplicate(entry: ConfigEntry) {
    setDuplicateSource(entry)
    setCreateOpen(true)
  }

  function closeCreate() {
    setCreateOpen(false)
    setDuplicateSource(null)
  }

  useEffect(() => {
    void refresh()
  }, [refresh])

  const visibleEntries: ConfigEntry[] = entries.filter((entry) => {
    // When the user hasn't filtered by Scope, "mine" and "shared" are
    // meaningless — show everything regardless of viewMode.
    if (!scope) {
      return true
    }
    if (viewMode === 'mine') {
      return entry.scope === scope
    }
    if (viewMode === 'shared') {
      return entry.scope !== scope
    }
    return true // 'all'
  })

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border px-6 py-4 flex items-center justify-between">
        <div className="flex items-center gap-6">
          <h1 className="text-xl font-semibold">DbConfig</h1>
          {header}
        </div>
        <div className="flex items-center gap-1">
          {headerExtras}
          <ThemeToggle />
        </div>
      </header>
      <main className="px-6 py-6 space-y-4">
        <AccessWarningBanner />
        <div className="flex items-center justify-between flex-wrap gap-4">
          <ScopeSelector />
          <div className="flex items-center gap-2">
            <ViewModeToggle />
            <ListModeToggle />
            <ReloadButton />
            <ExportButton />
            <Button size="sm" variant="outline" className="gap-1.5" onClick={() => setImportOpen(true)}>
              <Upload className="h-3.5 w-3.5" />
              Import
            </Button>
            <Button size="sm" onClick={() => setCreateOpen(true)} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              New Entry
            </Button>
          </div>
        </div>
        <TruncationBanner />
        <BulkActionsToolbar visibleEntries={visibleEntries} />
        {listMode === 'tree' ? (
          <EntriesTreeView
            onEdit={setEditingEntry}
            onDelete={setDeletingEntry}
            onHistory={setHistoryEntry}
            onDuplicate={handleDuplicate}
            visibleEntries={visibleEntries}
          />
        ) : (
          <EntriesTable
            onEdit={setEditingEntry}
            onDelete={setDeletingEntry}
            onHistory={setHistoryEntry}
            onDuplicate={handleDuplicate}
            visibleEntries={visibleEntries}
          />
        )}
      </main>
      <EditValueDialog
        entry={editingEntry}
        onClose={() => setEditingEntry(null)}
      />
      <CreateEntryDialog
        open={createOpen}
        onClose={closeCreate}
        sourceEntry={duplicateSource}
      />
      <DeleteEntryDialog
        entry={deletingEntry}
        onClose={() => setDeletingEntry(null)}
      />
      <EntryHistoryDialog
        open={!!historyEntry}
        onClose={() => setHistoryEntry(null)}
        scope={historyEntry?.scope ?? ''}
        environment={historyEntry?.environment ?? ''}
        tenantId={historyEntry?.tenantId ?? ''}
        entryKey={historyEntry?.key ?? ''}
        entryIsSecret={historyEntry?.isSecret ?? false}
      />
      <ImportDialog
        open={importOpen}
        onClose={() => setImportOpen(false)}
      />
    </div>
  )
}
