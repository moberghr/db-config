import { useState } from 'react'
import { diffChars } from 'diff'
import { Eye, EyeOff } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

interface ValueDiffProps {
  oldValue: string | null
  newValue: string | null
  isSecret: boolean
}

const SIZE_LIMIT = 100 * 1024 // 100KB

function tryPrettyJson(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

function looksLikeJson(value: string): boolean {
  const trimmed = value.trimStart()
  return trimmed.startsWith('{') || trimmed.startsWith('[')
}

function prepareValue(value: string | null): string {
  if (value === null) return ''
  if (looksLikeJson(value)) return tryPrettyJson(value)
  return value
}

interface ValueDiffContentProps {
  oldPrepared: string
  newPrepared: string
  oldValue: string | null
  newValue: string | null
  revealed: boolean
}

function ValueDiffContent({
  oldPrepared,
  newPrepared,
  oldValue,
  newValue,
  revealed,
}: ValueDiffContentProps) {
  const diffs = diffChars(oldPrepared, newPrepared)

  const oldNodes: React.ReactNode[] = []
  const newNodes: React.ReactNode[] = []

  diffs.forEach((part, i) => {
    if (part.added) {
      newNodes.push(
        <span
          key={i}
          className="bg-green-200 text-green-900 underline dark:bg-green-800/50 dark:text-green-200"
        >
          {part.value}
        </span>
      )
    } else if (part.removed) {
      oldNodes.push(
        <span
          key={i}
          className="bg-red-200 text-red-900 line-through dark:bg-red-800/50 dark:text-red-200"
        >
          {part.value}
        </span>
      )
    } else {
      // Unchanged — appears on both sides; create separate nodes to avoid key conflicts
      oldNodes.push(
        <span key={`old-${i}`} className="text-muted-foreground">
          {part.value}
        </span>
      )
      newNodes.push(
        <span key={`new-${i}`} className="text-muted-foreground">
          {part.value}
        </span>
      )
    }
  })

  const renderPane = (
    label: string,
    rawValue: string | null,
    nodes: React.ReactNode[],
    side: 'old' | 'new'
  ) => {
    const isEmpty = rawValue === null || rawValue === ''
    const displayLabel = rawValue === null ? '(deleted)' : '(empty)'

    return (
      <div className="flex-1 min-w-0">
        <div
          className={cn(
            'text-xs font-semibold px-2 py-1 rounded-t border-b',
            side === 'old'
              ? 'bg-red-50 text-red-700 border-red-200 dark:bg-red-950/30 dark:text-red-400 dark:border-red-900'
              : 'bg-green-50 text-green-700 border-green-200 dark:bg-green-950/30 dark:text-green-400 dark:border-green-900'
          )}
        >
          {label}
        </div>
        <div
          className={cn(
            'font-mono text-xs p-2 rounded-b min-h-[2rem] whitespace-pre-wrap break-all border border-t-0',
            side === 'old'
              ? 'bg-red-50/50 border-red-200 dark:bg-red-950/10 dark:border-red-900'
              : 'bg-green-50/50 border-green-200 dark:bg-green-950/10 dark:border-green-900'
          )}
        >
          {!revealed && rawValue !== null && rawValue !== '' ? (
            <span className="text-muted-foreground">••••••••</span>
          ) : isEmpty ? (
            <span className="text-muted-foreground italic">{displayLabel}</span>
          ) : (
            nodes
          )}
        </div>
      </div>
    )
  }

  return (
    <div className="flex gap-2">
      {renderPane('Before', oldValue, oldNodes, 'old')}
      {renderPane('After', newValue, newNodes, 'new')}
    </div>
  )
}

export function ValueDiff({ oldValue, newValue, isSecret }: ValueDiffProps) {
  const [revealed, setRevealed] = useState(false)

  const oldStr = oldValue ?? ''
  const newStr = newValue ?? ''

  // Size guard — avoid browser slowdown on huge values
  if (oldStr.length > SIZE_LIMIT || newStr.length > SIZE_LIMIT) {
    return (
      <div className="rounded border border-yellow-200 bg-yellow-50 px-3 py-2 text-xs text-yellow-800 dark:border-yellow-900 dark:bg-yellow-950/20 dark:text-yellow-400">
        Value too large to diff (100KB+)
      </div>
    )
  }

  const oldPrepared = prepareValue(oldValue)
  const newPrepared = prepareValue(newValue)

  return (
    <div className="space-y-2">
      {isSecret && (
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            className="h-7 text-xs"
            onClick={() => setRevealed((r) => !r)}
          >
            {revealed ? (
              <>
                <EyeOff className="h-3 w-3 mr-1" />
                Hide values
              </>
            ) : (
              <>
                <Eye className="h-3 w-3 mr-1" />
                Reveal values
              </>
            )}
          </Button>
          {!revealed && (
            <span className="text-xs text-muted-foreground">Secret values are masked</span>
          )}
        </div>
      )}
      <ValueDiffContent
        oldPrepared={oldPrepared}
        newPrepared={newPrepared}
        oldValue={oldValue}
        newValue={newValue}
        revealed={isSecret ? revealed : true}
      />
    </div>
  )
}
