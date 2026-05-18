import { useState } from 'react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog'

type ItemStatus = 'pending' | 'running' | 'done' | 'failed'

interface ItemState {
  label: string
  status: ItemStatus
  error?: string
}

export interface BulkOperationDialogProps<T> {
  open: boolean
  onClose: () => void
  title: string
  message: string
  items: T[]
  getLabel: (item: T) => string
  executeOne: (item: T) => Promise<void>
}

type Phase = 'confirm' | 'running' | 'done'

export function BulkOperationDialog<T>({
  open,
  onClose,
  title,
  message,
  items,
  getLabel,
  executeOne,
}: BulkOperationDialogProps<T>) {
  const [phase, setPhase] = useState<Phase>('confirm')
  const [itemStates, setItemStates] = useState<ItemState[]>([])

  function handleOpenChange(o: boolean) {
    if (!o && phase !== 'running') {
      handleClose()
    }
  }

  function handleClose() {
    setPhase('confirm')
    setItemStates([])
    onClose()
  }

  async function handleConfirm() {
    const initial: ItemState[] = items.map((item) => ({
      label: getLabel(item),
      status: 'pending',
    }))
    setItemStates(initial)
    setPhase('running')

    const states = [...initial]
    for (let i = 0; i < items.length; i++) {
      states[i] = { ...states[i], status: 'running' }
      setItemStates([...states])
      try {
        await executeOne(items[i])
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
      setItemStates([...states])
    }

    setPhase('done')
  }

  const successCount = itemStates.filter((s) => s.status === 'done').length
  const failCount = itemStates.filter((s) => s.status === 'failed').length

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {phase === 'confirm' && (
            <DialogDescription>{message}</DialogDescription>
          )}
        </DialogHeader>

        {(phase === 'running' || phase === 'done') && (
          <div className="max-h-64 overflow-y-auto space-y-1 py-2">
            {itemStates.map((item, idx) => (
              <div key={idx} className="flex items-start gap-2 text-sm">
                <span className="mt-0.5 shrink-0">
                  {item.status === 'pending' && <span className="text-muted-foreground">○</span>}
                  {item.status === 'running' && <span className="text-primary animate-pulse">●</span>}
                  {item.status === 'done' && <span className="text-green-600 dark:text-green-400">✓</span>}
                  {item.status === 'failed' && <span className="text-destructive">✗</span>}
                </span>
                <span className="font-mono text-xs break-all">{item.label}</span>
                {item.status === 'failed' && item.error && (
                  <span className="text-xs text-destructive ml-auto shrink-0">{item.error}</span>
                )}
              </div>
            ))}
          </div>
        )}

        {phase === 'done' && (
          <p className="text-sm text-muted-foreground">
            {successCount} succeeded, {failCount} failed.
          </p>
        )}

        <DialogFooter>
          {phase === 'confirm' && (
            <>
              <Button variant="outline" onClick={handleClose}>
                Cancel
              </Button>
              <Button onClick={() => { void handleConfirm() }}>
                Confirm
              </Button>
            </>
          )}
          {phase === 'running' && (
            <Button variant="outline" disabled>
              Running…
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
