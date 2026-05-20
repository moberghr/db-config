import { useCallback, useEffect, useState } from 'react'
import { EntriesPage } from './pages/EntriesPage'
import { AuditLogPage } from './pages/AuditLogPage'
import { LoginPage } from './pages/LoginPage'
import { SignOutButton } from './components/SignOutButton'
import { fetchAuthStatus } from './api/auth'
import { cn } from '@/lib/utils'

type Tab = 'entries' | 'audit'

type AuthState =
  | { kind: 'checking' }
  | { kind: 'login' }
  | { kind: 'authenticated'; hasBuiltInLogin: boolean }

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
  const [auth, setAuth] = useState<AuthState>({ kind: 'checking' })

  const checkAuth = useCallback(async () => {
    const status = await fetchAuthStatus()
    if (status.authenticated) {
      setAuth({ kind: 'authenticated', hasBuiltInLogin: status.hasBuiltInLogin })
    } else {
      setAuth({ kind: 'login' })
    }
  }, [])

  useEffect(() => {
    void checkAuth()
  }, [checkAuth])

  if (auth.kind === 'checking') {
    // Empty shell that respects the persisted theme so there's no flash.
    return <div className="min-h-screen bg-background" aria-busy="true" />
  }

  if (auth.kind === 'login') {
    return <LoginPage onLoginSuccess={checkAuth} />
  }

  const header = <TabSwitcher active={tab} onChange={setTab} />
  const headerExtras = auth.hasBuiltInLogin
    ? <SignOutButton onSignedOut={checkAuth} />
    : null

  if (tab === 'audit') {
    return <AuditLogPage header={header} headerExtras={headerExtras} />
  }

  return <EntriesPage header={header} headerExtras={headerExtras} />
}

export default App
