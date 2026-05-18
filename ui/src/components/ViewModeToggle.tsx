import { useScopeStore } from '@/store/scopeStore'
import { cn } from '@/lib/utils'

type ViewMode = 'mine' | 'shared' | 'all'

const MODES: { value: ViewMode; label: string }[] = [
  { value: 'mine', label: 'Mine' },
  { value: 'shared', label: 'Shared' },
  { value: 'all', label: 'All' },
]

export function ViewModeToggle() {
  const viewMode = useScopeStore((s) => s.viewMode)
  const setViewMode = useScopeStore((s) => s.setViewMode)

  return (
    <div className="inline-flex items-center rounded-md border border-input bg-background p-0.5 gap-0.5">
      {MODES.map(({ value, label }) => (
        <button
          key={value}
          type="button"
          onClick={() => setViewMode(value)}
          className={cn(
            'rounded px-3 py-1 text-xs font-medium transition-colors',
            viewMode === value
              ? 'bg-primary text-primary-foreground shadow-sm'
              : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
          )}
        >
          {label}
        </button>
      ))}
    </div>
  )
}
