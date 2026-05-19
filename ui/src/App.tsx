import { useState } from 'react'
import { EntriesPage } from './pages/EntriesPage'
import { AuditLogPage } from './pages/AuditLogPage'
import { cn } from '@/lib/utils'

type Tab = 'entries' | 'audit'

function TabSwitcher({
  active,
  onChange,
}: {
  active: Tab
  onChange: (tab: Tab) => void
}) {
  return (
    <nav className="flex items-center gap-1" aria-label="Main navigation">
      <button
        type="button"
        onClick={() => onChange('entries')}
        className={cn(
          'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
          active === 'entries'
            ? 'bg-muted text-foreground'
            : 'text-muted-foreground hover:bg-muted/50 hover:text-foreground',
        )}
      >
        Entries
      </button>
      <button
        type="button"
        onClick={() => onChange('audit')}
        className={cn(
          'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
          active === 'audit'
            ? 'bg-muted text-foreground'
            : 'text-muted-foreground hover:bg-muted/50 hover:text-foreground',
        )}
      >
        Audit Log
      </button>
    </nav>
  )
}

function App() {
  const [tab, setTab] = useState<Tab>('entries')
  const header = <TabSwitcher active={tab} onChange={setTab} />

  if (tab === 'audit') {
    return <AuditLogPage header={header} />
  }
  return <EntriesPage header={header} />
}

export default App
