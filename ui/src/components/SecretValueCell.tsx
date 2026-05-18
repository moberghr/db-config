import { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { Button } from '@/components/ui/button'

interface SecretValueCellProps {
  value: string | null
  isSecret: boolean
}

export function SecretValueCell({ value, isSecret }: SecretValueCellProps) {
  const [revealed, setRevealed] = useState(false)

  if (value === null) {
    return <span className="text-muted-foreground italic">(empty)</span>
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
