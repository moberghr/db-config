import { useEffect, useState } from 'react'
import type { ConfigAuditAction, ConfigAuditEntry } from '@/api/entries'
import { queryAuditEntries } from '@/api/audit'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table'
import { AccessWarningBanner } from '@/components/AccessWarningBanner'
import { ThemeToggle } from '@/components/ThemeToggle'
import { EntryHistoryDialog } from '@/components/EntryHistoryDialog'
import { Eye, EyeOff, RefreshCw } from 'lucide-react'
import { cn } from '@/lib/utils'

const ACTION_STYLES: Record<ConfigAuditAction, string> = {
  Insert: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-200',
  Update: 'bg-blue-100 text-blue-800 dark:bg-blue-900/40 dark:text-blue-200',
  Delete: 'bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-200',
  Read: 'bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-200',
}

function ActionChip({ action }: { action: ConfigAuditAction }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        ACTION_STYLES[action],
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
    return <span className="font-mono text-xs break-all">{value}</span>
  }

  return (
    <span className="flex items-center gap-1">
      <span className="font-mono text-xs break-all">{revealed ? value : '••••••••'}</span>
      <Button
        variant="ghost"
        size="icon"
        className="h-6 w-6 flex-shrink-0"
        onClick={(e) => {
          e.stopPropagation()
          setRevealed((r) => !r)
        }}
        title={revealed ? 'Hide value' : 'Reveal value'}
      >
        {revealed ? <EyeOff className="h-3 w-3" /> : <Eye className="h-3 w-3" />}
      </Button>
    </span>
  )
}

interface AuditLogPageProps {
  header?: React.ReactNode
}

export function AuditLogPage({ header }: AuditLogPageProps) {
  const [entries, setEntries] = useState<ConfigAuditEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [appNameFilter, setAppNameFilter] = useState('')
  const [environmentFilter, setEnvironmentFilter] = useState('')
  const [tenantFilter, setTenantFilter] = useState('')
  const [keyPrefixFilter, setKeyPrefixFilter] = useState('')
  const [actionFilter, setActionFilter] = useState<ConfigAuditAction | ''>('')

  const [historyEntry, setHistoryEntry] = useState<ConfigAuditEntry | null>(null)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      const data = await queryAuditEntries({
        appName: appNameFilter || undefined,
        environment: environmentFilter || undefined,
        tenantId: tenantFilter || undefined,
        keyPrefix: keyPrefixFilter || undefined,
        action: actionFilter || undefined,
      })
      setEntries(data)
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to load audit log'
      setError(message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
    // Intentionally only on mount; filters re-trigger via the Apply button.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border px-6 py-4 flex items-center justify-between">
        <div className="flex items-center gap-6">
          <h1 className="text-xl font-semibold">DbConfig</h1>
          {header}
        </div>
        <ThemeToggle />
      </header>
      <main className="px-6 py-6 space-y-4">
        <AccessWarningBanner />
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-1">
            <label className="text-xs text-muted-foreground">AppName</label>
            <Input
              value={appNameFilter}
              onChange={(e) => setAppNameFilter(e.target.value)}
              placeholder="all"
              className="h-8 w-40"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs text-muted-foreground">Environment</label>
            <Input
              value={environmentFilter}
              onChange={(e) => setEnvironmentFilter(e.target.value)}
              placeholder="all"
              className="h-8 w-32"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs text-muted-foreground">Tenant</label>
            <Input
              value={tenantFilter}
              onChange={(e) => setTenantFilter(e.target.value)}
              placeholder="all"
              className="h-8 w-32"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs text-muted-foreground">Key prefix</label>
            <Input
              value={keyPrefixFilter}
              onChange={(e) => setKeyPrefixFilter(e.target.value)}
              placeholder="e.g. Stripe:"
              className="h-8 w-48"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs text-muted-foreground">Action</label>
            <select
              value={actionFilter}
              onChange={(e) => setActionFilter(e.target.value as ConfigAuditAction | '')}
              className="h-8 rounded-md border border-input bg-background px-2 text-sm"
            >
              <option value="">All</option>
              <option value="Insert">Insert</option>
              <option value="Update">Update</option>
              <option value="Delete">Delete</option>
              <option value="Read">Read</option>
            </select>
          </div>
          <Button size="sm" onClick={() => void load()} className="gap-1.5">
            <RefreshCw className="h-3.5 w-3.5" />
            Apply
          </Button>
        </div>

        {loading && (
          <div className="flex items-center justify-center py-16 text-muted-foreground text-sm">
            Loading audit log…
          </div>
        )}

        {!loading && error && (
          <div className="rounded-md border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive">
            {error}
          </div>
        )}

        {!loading && !error && entries.length === 0 && (
          <div className="flex items-center justify-center py-16 text-muted-foreground text-sm">
            No audit entries match the current filters.
          </div>
        )}

        {!loading && !error && entries.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-24">Action</TableHead>
                <TableHead>Key</TableHead>
                <TableHead>Old Value</TableHead>
                <TableHead>New Value</TableHead>
                <TableHead>AppName</TableHead>
                <TableHead>Environment</TableHead>
                <TableHead>Tenant</TableHead>
                <TableHead>Modified UTC</TableHead>
                <TableHead>Modified By</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {entries.map((entry) => (
                <TableRow
                  key={entry.id}
                  className="cursor-pointer"
                  onClick={() => setHistoryEntry(entry)}
                  title="Open full history for this key"
                >
                  <TableCell>
                    <ActionChip action={entry.action} />
                  </TableCell>
                  <TableCell className="font-mono text-xs font-medium">{entry.key}</TableCell>
                  <TableCell onClick={(e) => e.stopPropagation()}>
                    <AuditValueCell value={entry.oldValue} isSecret={entry.isSecret} />
                  </TableCell>
                  <TableCell onClick={(e) => e.stopPropagation()}>
                    <AuditValueCell value={entry.newValue} isSecret={entry.isSecret} />
                  </TableCell>
                  <TableCell>
                    <span className="text-xs text-foreground">{entry.appName}</span>
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
                  <TableCell className="text-muted-foreground text-xs whitespace-nowrap">
                    {new Date(entry.modifiedUtc).toLocaleString()}
                  </TableCell>
                  <TableCell className="text-muted-foreground text-xs">
                    {entry.modifiedBy ?? '—'}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </main>
      <EntryHistoryDialog
        open={!!historyEntry}
        onClose={() => setHistoryEntry(null)}
        appName={historyEntry?.appName ?? ''}
        environment={historyEntry?.environment ?? ''}
        tenantId={historyEntry?.tenantId ?? ''}
        entryKey={historyEntry?.key ?? ''}
        entryIsSecret={historyEntry?.isSecret ?? false}
      />
    </div>
  )
}
