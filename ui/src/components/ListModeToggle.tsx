import { useScopeStore } from '@/store/scopeStore'
import { cn } from '@/lib/utils'
import { Table, ListTree } from 'lucide-react'

type ListMode = 'flat' | 'tree'

const MODES: { value: ListMode; label: string; Icon: typeof Table }[] = [
  { value: 'flat', label: 'Flat', Icon: Table },
  { value: 'tree', label: 'Tree', Icon: ListTree },
]

export function ListModeToggle() {
  const listMode = useScopeStore((s) => s.listMode)
  const setListMode = useScopeStore((s) => s.setListMode)

  return (
    <div className="inline-flex items-center rounded-md border border-input bg-background p-0.5 gap-0.5">
      {MODES.map(({ value, label, Icon }) => (
        <button
          key={value}
          type="button"
          onClick={() => setListMode(value)}
          className={cn(
            'inline-flex items-center gap-1.5 rounded px-3 py-1 text-xs font-medium transition-colors',
            listMode === value
              ? 'bg-primary text-primary-foreground shadow-sm'
              : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
          )}
        >
          <Icon className="h-3.5 w-3.5" />
          {label}
        </button>
      ))}
    </div>
  )
}
