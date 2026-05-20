import type { ConfigEntry } from '@/api/entries'
import { useEntriesStore } from '@/store/entriesStore'
import { useScopeStore } from '@/store/scopeStore'
import { SecretValueCell } from './SecretValueCell'
import { Button } from '@/components/ui/button'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table'
import { Copy, History, Pencil, Trash2 } from 'lucide-react'
import { cn } from '@/lib/utils'

const CROSS_SCOPE_TITLE =
  'Cross-scope edits are not allowed in this UI. Switch to that scope or use a host with platform-admin access.'

function compositeKey(entry: ConfigEntry): string {
  return `${entry.scope}|${entry.environment}|${entry.tenantId}|${entry.key}`
}

interface EntriesTableProps {
  onEdit: (entry: ConfigEntry) => void
  onDelete: (entry: ConfigEntry) => void
  onHistory: (entry: ConfigEntry) => void
  onDuplicate: (entry: ConfigEntry) => void
  visibleEntries: ConfigEntry[]
}

export function EntriesTable({ onEdit, onDelete, onHistory, onDuplicate, visibleEntries }: EntriesTableProps) {
  const loading = useEntriesStore((s) => s.loading)
  const error = useEntriesStore((s) => s.error)
  const selectedKeys = useEntriesStore((s) => s.selectedKeys)
  const toggleSelection = useEntriesStore((s) => s.toggleSelection)
  const selectAll = useEntriesStore((s) => s.selectAll)
  const clearSelection = useEntriesStore((s) => s.clearSelection)
  const currentScope = useScopeStore((s) => s.scope)

  const allCompositeKeys = visibleEntries.map(compositeKey)
  const allSelected = allCompositeKeys.length > 0 && allCompositeKeys.every((k) => selectedKeys.has(k))
  const someSelected = allCompositeKeys.some((k) => selectedKeys.has(k))

  if (loading) {
    return (
      <div className="flex items-center justify-center py-16 text-muted-foreground text-sm">
        Loading entries…
      </div>
    )
  }

  if (error) {
    return (
      <div className="rounded-md border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive">
        {error}
      </div>
    )
  }

  if (visibleEntries.length === 0) {
    return (
      <div className="flex items-center justify-center py-16 text-muted-foreground text-sm">
        No entries found. Use the &ldquo;New Entry&rdquo; button to add one.
      </div>
    )
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-10">
            <input
              type="checkbox"
              className="h-4 w-4 rounded border border-input accent-primary"
              checked={allSelected}
              ref={(el) => {
                if (el) el.indeterminate = !allSelected && someSelected
              }}
              onChange={() => {
                if (allSelected) {
                  clearSelection()
                } else {
                  selectAll(allCompositeKeys)
                }
              }}
              aria-label="Select all"
            />
          </TableHead>
          <TableHead>Key</TableHead>
          <TableHead>Value</TableHead>
          <TableHead>Scope</TableHead>
          <TableHead>Environment</TableHead>
          <TableHead>Tenant</TableHead>
          <TableHead>Modified</TableHead>
          <TableHead>Modified By</TableHead>
          <TableHead className="w-28 text-right">Actions</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {visibleEntries.map((entry) => {
          // "Own" means the entry's Scope matches the currently-filtered scope.
          // When no scope filter is set (multi-scope mode), every entry is editable.
          const isOwn = !currentScope || entry.scope === currentScope
          const ck = compositeKey(entry)
          const isSelected = selectedKeys.has(ck)
          return (
            <TableRow
              key={ck}
              className={cn('cursor-pointer', !isOwn && 'opacity-80', isSelected && 'bg-primary/5')}
              onClick={() => { if (isOwn) onEdit(entry) }}
            >
              <TableCell onClick={(e) => e.stopPropagation()}>
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded border border-input accent-primary"
                  checked={isSelected}
                  onChange={() => toggleSelection(ck)}
                  aria-label={`Select ${entry.key}`}
                />
              </TableCell>
              <TableCell className="font-mono text-xs font-medium">{entry.key}</TableCell>
              <TableCell>
                <SecretValueCell value={entry.value} isSecret={entry.isSecret} />
              </TableCell>
              <TableCell>
                <span className="text-xs text-foreground">{entry.scope}</span>
              </TableCell>
              <TableCell>
                <span className="text-xs text-foreground">{entry.environment}</span>
              </TableCell>
              <TableCell>
                {entry.tenantId ? (
                  <span className="text-xs text-foreground">{entry.tenantId}</span>
                ) : (
                  <span className="text-xs text-muted-foreground italic">default</span>
                )}
              </TableCell>
              <TableCell className="text-muted-foreground text-xs">
                {new Date(entry.modifiedUtc).toLocaleString()}
              </TableCell>
              <TableCell className="text-muted-foreground text-xs">
                {entry.modifiedBy ?? '—'}
              </TableCell>
              <TableCell className="text-right" onClick={(e) => e.stopPropagation()}>
                <span className="flex items-center justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7"
                    title="History"
                    onClick={() => onHistory(entry)}
                  >
                    <History className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7"
                    title="Duplicate — opens a Create dialog pre-filled from this entry"
                    onClick={() => onDuplicate(entry)}
                  >
                    <Copy className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7"
                    title={isOwn ? 'Edit' : CROSS_SCOPE_TITLE}
                    disabled={!isOwn}
                    onClick={() => { if (isOwn) onEdit(entry) }}
                  >
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7 text-destructive hover:text-destructive"
                    title={isOwn ? 'Delete' : CROSS_SCOPE_TITLE}
                    disabled={!isOwn}
                    onClick={() => { if (isOwn) onDelete(entry) }}
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </span>
              </TableCell>
            </TableRow>
          )
        })}
      </TableBody>
    </Table>
  )
}
