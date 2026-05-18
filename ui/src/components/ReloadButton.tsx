import { useState } from 'react'
import { useEntriesStore } from '@/store/entriesStore'
import { Button } from '@/components/ui/button'
import { RefreshCw } from 'lucide-react'

export function ReloadButton() {
  const reload = useEntriesStore((s) => s.reload)
  const [state, setState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')

  async function handleReload() {
    setState('loading')
    try {
      await reload()
      setState('done')
      setTimeout(() => setState('idle'), 2000)
    } catch {
      setState('error')
      setTimeout(() => setState('idle'), 3000)
    }
  }

  return (
    <Button
      variant="outline"
      size="sm"
      onClick={() => { void handleReload() }}
      disabled={state === 'loading'}
      className="gap-1.5"
    >
      <RefreshCw className={`h-3.5 w-3.5 ${state === 'loading' ? 'animate-spin' : ''}`} />
      {state === 'done' ? 'Reloaded!' : state === 'error' ? 'Error' : 'Force Reload'}
    </Button>
  )
}
