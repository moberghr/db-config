import { useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog'
import { listEntries, upsertEntry } from '@/api/entries'
import { useScopeStore } from '@/store/scopeStore'
import { useEntriesStore } from '@/store/entriesStore'
import { nestedToFlat } from '@/lib/appsettings'
import type { FlatImportEntry } from '@/lib/appsettings'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table'

type CollisionPolicy = 'overwrite' | 'skip' | 'error'

type ItemStatus = 'pending' | 'running' | 'done' | 'failed' | 'skipped'

interface ItemState {
  key: string
  status: ItemStatus
  error?: string
}

type Phase = 'pick' | 'preview' | 'running' | 'done'

interface ImportDialogProps {
  open: boolean
  onClose: () => void
}

export function ImportDialog({ open, onClose }: ImportDialogProps) {
  const appName = useScopeStore((s) => s.appName)
  const environment = useScopeStore((s) => s.environment)
  const refresh = useEntriesStore((s) => s.refresh)

  const fileInputRef = useRef<HTMLInputElement>(null)

  const [phase, setPhase] = useState<Phase>('pick')
  const [parseError, setParseError] = useState<string | null>(null)
  const [parsed, setParsed] = useState<FlatImportEntry[]>([])
  const [collisionPolicy, setCollisionPolicy] = useState<CollisionPolicy>('overwrite')
  const [itemStates, setItemStates] = useState<ItemState[]>([])
  const [collisionKeys, setCollisionKeys] = useState<string[]>([])

  function handleClose() {
    setPhase('pick')
    setParseError(null)
    setParsed([])
    setCollisionPolicy('overwrite')
    setItemStates([])
    setCollisionKeys([])
    if (fileInputRef.current) {
      fileInputRef.current.value = ''
    }
    onClose()
  }

  function handleOpenChange(o: boolean) {
    if (!o && phase !== 'running') {
      handleClose()
    }
  }

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return

    setParseError(null)
    const reader = new FileReader()
    reader.onload = (evt) => {
      try {
        const text = evt.target?.result as string
        const json: unknown = JSON.parse(text)
        const entries = nestedToFlat(json)
        if (entries.length === 0) {
          setParseError('No importable entries found in the selected file.')
          return
        }
        setParsed(entries)
        setCollisionKeys([])
        setPhase('preview')
      } catch {
        setParseError('Failed to parse file as JSON. Please select a valid JSON file.')
      }
    }
    reader.readAsText(file)
  }

  async function handleImport() {
    if (!appName || !environment) return

    // Dry-run: fetch current keys for collision detection
    let existingKeys = new Set<string>()
    if (collisionPolicy === 'skip' || collisionPolicy === 'error') {
      const existing = await listEntries(appName, environment)
      existingKeys = new Set(existing.map((e) => e.key))
    }

    // Check for collisions when policy is 'error'
    if (collisionPolicy === 'error') {
      const collisions = parsed.filter((e) => existingKeys.has(e.key)).map((e) => e.key)
      if (collisions.length > 0) {
        setCollisionKeys(collisions)
        return
      }
    }

    // Determine which entries to actually write
    const toImport =
      collisionPolicy === 'skip'
        ? parsed.filter((e) => !existingKeys.has(e.key))
        : parsed

    const initial: ItemState[] = toImport.map((e) => ({ key: e.key, status: 'pending' }))
    // Entries that were skipped due to 'skip' policy get a skipped state in the final list
    const skippedItems: ItemState[] =
      collisionPolicy === 'skip'
        ? parsed
            .filter((e) => existingKeys.has(e.key))
            .map((e) => ({ key: e.key, status: 'skipped' as ItemStatus }))
        : []

    setItemStates([...initial, ...skippedItems])
    setCollisionKeys([])
    setPhase('running')

    const states = [...initial]
    for (let i = 0; i < toImport.length; i++) {
      const entry = toImport[i]
      states[i] = { ...states[i], status: 'running' }
      setItemStates([...states, ...skippedItems])
      try {
        await upsertEntry(appName, environment, entry.key, entry.value, entry.isSecret)
        states[i] = { ...states[i], status: 'done' }
      } catch (err: unknown) {
        let errorMsg = err instanceof Error ? err.message : 'Failed'
        if (typeof err === 'object' && err !== null && 'response' in err) {
          const resp = (err as { response?: { status?: number; data?: { detail?: string } } }).response
          if (resp?.status === 403) {
            errorMsg = '403 Forbidden'
          } else if (resp?.data?.detail) {
            errorMsg = resp.data.detail
          }
        }
        states[i] = { ...states[i], status: 'failed', error: errorMsg }
      }
      setItemStates([...states, ...skippedItems])
    }

    setPhase('done')
    void refresh()
  }

  const successCount = itemStates.filter((s) => s.status === 'done').length
  const failCount = itemStates.filter((s) => s.status === 'failed').length
  const skippedCount = itemStates.filter((s) => s.status === 'skipped').length

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent size="xl">
        <DialogHeader>
          <DialogTitle>Import entries</DialogTitle>
          {phase === 'pick' && (
            <DialogDescription>
              Select an appsettings-shaped JSON file (e.g. exported from DbConfig) to import
              into <strong>{appName || '(no app selected)'}</strong> / <strong>{environment || '(no env)'}</strong>.
            </DialogDescription>
          )}
          {phase === 'preview' && (
            <DialogDescription>
              Review the {parsed.length} entries to be imported. Choose a collision policy, then click Import.
            </DialogDescription>
          )}
        </DialogHeader>

        {/* Phase: pick */}
        {phase === 'pick' && (
          <div className="space-y-3">
            <input
              ref={fileInputRef}
              type="file"
              accept=".json,application/json"
              className="block w-full text-sm text-muted-foreground
                file:mr-3 file:py-1.5 file:px-3 file:rounded-md file:border
                file:border-input file:text-sm file:font-medium
                file:bg-background file:text-foreground
                hover:file:bg-muted cursor-pointer"
              onChange={handleFileChange}
            />
            {parseError && <p className="text-sm text-destructive">{parseError}</p>}
          </div>
        )}

        {/* Phase: preview */}
        {phase === 'preview' && (
          <div className="space-y-4">
            {/* Collision policy */}
            <fieldset className="space-y-1">
              <legend className="text-sm font-medium">Collision policy</legend>
              {(
                [
                  { value: 'overwrite', label: 'Overwrite existing' },
                  { value: 'skip', label: 'Skip existing (only import new keys)' },
                  { value: 'error', label: 'Error on collision (abort if any key already exists)' },
                ] as { value: CollisionPolicy; label: string }[]
              ).map(({ value, label }) => (
                <label key={value} className="flex items-center gap-2 text-sm cursor-pointer">
                  <input
                    type="radio"
                    name="collision-policy"
                    value={value}
                    checked={collisionPolicy === value}
                    onChange={() => { setCollisionPolicy(value); setCollisionKeys([]) }}
                    className="accent-primary"
                  />
                  {label}
                </label>
              ))}
            </fieldset>

            {/* Collision error */}
            {collisionKeys.length > 0 && (
              <div className="rounded-md border border-destructive bg-destructive/10 p-3 space-y-1">
                <p className="text-sm font-medium text-destructive">
                  {collisionKeys.length} key{collisionKeys.length !== 1 ? 's' : ''} already exist.
                  Change the collision policy or remove them from your file.
                </p>
                <ul className="text-xs font-mono text-destructive space-y-0.5 max-h-24 overflow-y-auto">
                  {collisionKeys.map((k) => (
                    <li key={k}>{k}</li>
                  ))}
                </ul>
              </div>
            )}

            {/* Preview table */}
            <div className="max-h-64 overflow-y-auto rounded-md border border-border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Key</TableHead>
                    <TableHead>Value preview</TableHead>
                    <TableHead>Secret</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {parsed.map((entry) => (
                    <TableRow key={entry.key}>
                      <TableCell className="font-mono text-xs max-w-[200px] truncate">{entry.key}</TableCell>
                      <TableCell className="font-mono text-xs max-w-[200px] truncate text-muted-foreground">
                        {entry.isSecret ? '••••••••' : (entry.value.length > 60 ? entry.value.slice(0, 60) + '…' : entry.value)}
                      </TableCell>
                      <TableCell className="text-xs">{entry.isSecret ? 'Yes' : 'No'}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </div>
        )}

        {/* Phases: running / done */}
        {(phase === 'running' || phase === 'done') && (
          <div className="space-y-3">
            <div className="max-h-64 overflow-y-auto space-y-1 py-2">
              {itemStates.map((item, idx) => (
                <div key={idx} className="flex items-start gap-2 text-sm">
                  <span className="mt-0.5 shrink-0">
                    {item.status === 'pending' && <span className="text-muted-foreground">○</span>}
                    {item.status === 'running' && <span className="text-primary animate-pulse">●</span>}
                    {item.status === 'done' && <span className="text-green-600 dark:text-green-400">✓</span>}
                    {item.status === 'failed' && <span className="text-destructive">✗</span>}
                    {item.status === 'skipped' && <span className="text-muted-foreground">—</span>}
                  </span>
                  <span className="font-mono text-xs break-all">{item.key}</span>
                  {item.status === 'failed' && item.error && (
                    <span className="text-xs text-destructive ml-auto shrink-0">{item.error}</span>
                  )}
                  {item.status === 'skipped' && (
                    <span className="text-xs text-muted-foreground ml-auto shrink-0">skipped</span>
                  )}
                </div>
              ))}
            </div>
            {phase === 'done' && (
              <p className="text-sm text-muted-foreground">
                {successCount} imported, {skippedCount} skipped, {failCount} failed.
              </p>
            )}
          </div>
        )}

        <DialogFooter>
          {phase === 'pick' && (
            <Button variant="outline" onClick={handleClose}>
              Cancel
            </Button>
          )}
          {phase === 'preview' && (
            <>
              <Button variant="outline" onClick={() => { setPhase('pick'); setCollisionKeys([]) }}>
                Back
              </Button>
              <Button onClick={() => { void handleImport() }}>
                Import {parsed.length} {parsed.length === 1 ? 'entry' : 'entries'}
              </Button>
            </>
          )}
          {phase === 'running' && (
            <Button variant="outline" disabled>
              Importing…
            </Button>
          )}
          {phase === 'done' && (
            <Button onClick={handleClose}>Close</Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
