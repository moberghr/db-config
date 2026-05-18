import { useEffect, useState } from 'react'
import { Eye, EyeOff, GitCompareArrows } from 'lucide-react'
import type { ConfigAuditAction, ConfigAuditEntry } from '@/api/entries'
import { getAuditHistory } from '@/api/entries'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table'
import { cn } from '@/lib/utils'
import { ValueDiff } from '@/components/ValueDiff'

interface EntryHistoryDialogProps {
  open: boolean
  onClose: () => void
  appName: string
  environment: string
  entryKey: string
  entryIsSecret: boolean
}

function ActionChip({ action }: { action: ConfigAuditAction }) {
  const styles: Record<ConfigAuditAction, string> = {
    Insert: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-200',
    Update: 'bg-blue-100 text-blue-800 dark:bg-blue-900/40 dark:text-blue-200',
    Delete: 'bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-200',
  }
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        styles[action]
      )}
    >
      {action}
    </span>
  )
}

function AuditValueCell({ value, isSecret }: { value: string | null; isSecret: boolean }) {
  const [revealed, setRevealed] = useState(false)

  if (value === null) {
    return <span className="text-muted-foreground italic">—</span>
  }

  if (!isSecret) {
    return <span className="font-mono text-xs">{value}</span>
  }

  return (
    <span className="flex items-center gap-1">
      <span className="font-mono text-xs">{revealed ? value : '••••••••'}</span>
      <Button
        variant="ghost"
        size="icon"
        className="h-6 w-6"
        onClick={() => setRevealed((r) => !r)}
        title={revealed ? 'Hide value' : 'Reveal value'}
      >
        {revealed ? <EyeOff className="h-3 w-3" /> : <Eye className="h-3 w-3" />}
      </Button>
    </span>
  )
}

export function EntryHistoryDialog({
  open,
  onClose,
  appName,
  environment,
  entryKey,
  entryIsSecret,
}: EntryHistoryDialogProps) {
  const [loading, setLoading] = useState(false)
  const [history, setHistory] = useState<ConfigAuditEntry[]>([])
  const [error, setError] = useState<string | null>(null)
  const [expandedRowId, setExpandedRowId] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setLoading(true)
    setError(null)
    setHistory([])
    getAuditHistory(appName, environment, entryKey, 50)
      .then((data) => setHistory(data))
      .catch((err: unknown) => {
        const message = err instanceof Error ? err.message : 'Failed to load history'
        setError(message)
      })
      .finally(() => setLoading(false))
  }, [open, appName, environment, entryKey])

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent size="xl">
        <DialogHeader>
          <DialogTitle>History — {entryKey}</DialogTitle>
        </DialogHeader>
        <div className="overflow-auto max-h-[60vh]">
          {loading && (
            <div className="flex items-center justify-center py-12 text-muted-foreground text-sm">
              Loading…
            </div>
          )}
          {!loading && error && (
            <div className="rounded-md border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive">
              {error}
            </div>
          )}
          {!loading && !error && history.length === 0 && (
            <div className="flex items-center justify-center py-12 text-muted-foreground text-sm">
              No audit history yet
            </div>
          )}
          {!loading && !error && history.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Action</TableHead>
                  <TableHead>Modified UTC</TableHead>
                  <TableHead>Modified By</TableHead>
                  <TableHead>Old Value</TableHead>
                  <TableHead>New Value</TableHead>
                  <TableHead className="w-[90px]">Compare</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {history.map((entry) => {
                  const isExpanded = expandedRowId === entry.id
                  return (
                    <>
                      <TableRow key={entry.id}>
                        <TableCell>
                          <ActionChip action={entry.action} />
                        </TableCell>
                        <TableCell className="text-muted-foreground text-xs whitespace-nowrap">
                          {new Date(entry.modifiedUtc).toLocaleString()}
                        </TableCell>
                        <TableCell className="text-muted-foreground text-xs">
                          {entry.modifiedBy ?? '—'}
                        </TableCell>
                        <TableCell onClick={(e) => e.stopPropagation()}>
                          <AuditValueCell value={entry.oldValue} isSecret={entryIsSecret} />
                        </TableCell>
                        <TableCell onClick={(e) => e.stopPropagation()}>
                          <AuditValueCell value={entry.newValue} isSecret={entryIsSecret} />
                        </TableCell>
                        <TableCell onClick={(e) => e.stopPropagation()}>
                          <Button
                            variant={isExpanded ? 'secondary' : 'ghost'}
                            size="sm"
                            className="h-7 text-xs"
                            onClick={() =>
                              setExpandedRowId(isExpanded ? null : entry.id)
                            }
                            title={isExpanded ? 'Collapse diff' : 'Compare old vs new value'}
                          >
                            <GitCompareArrows className={cn('h-3 w-3 mr-1', isExpanded && 'text-primary')} />
                            {isExpanded ? 'Close' : 'Compare'}
                          </Button>
                        </TableCell>
                      </TableRow>
                      {isExpanded && (
                        <TableRow key={`${entry.id}-diff`}>
                          <TableCell colSpan={6} className="bg-muted/30 pb-4 pt-2 px-4">
                            <div className="text-xs text-muted-foreground mb-2 font-medium">
                              Character-level diff
                            </div>
                            <ValueDiff
                              oldValue={entry.oldValue}
                              newValue={entry.newValue}
                              isSecret={entryIsSecret}
                            />
                          </TableCell>
                        </TableRow>
                      )}
                    </>
                  )
                })}
              </TableBody>
            </Table>
          )}
        </div>
        <div className="flex justify-end mt-4">
          <Button variant="outline" onClick={onClose}>
            Close
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}
