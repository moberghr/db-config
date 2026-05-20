import { useEntriesStore, DefaultQueryTake } from '@/store/entriesStore'
import { AlertCircle } from 'lucide-react'

/**
 * Surfaced when the flat-query response was capped at the server's default `take` limit
 * (mirrored client-side as <c>DefaultQueryTake</c>). The actual DB may hold more rows;
 * the user needs to narrow filters or accept that the table view is partial.
 */
export function TruncationBanner() {
  const truncated = useEntriesStore((s) => s.truncated)

  if (!truncated) {
    return null
  }

  return (
    <div className="flex items-start gap-2 rounded-md border border-amber-500/30 bg-amber-500/5 px-3 py-2 text-xs text-amber-700 dark:text-amber-300">
      <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
      <div>
        Showing the first {DefaultQueryTake} entries. The database may contain more —
        narrow with the Scope / Environment / Tenant filters above to see the rest.
      </div>
    </div>
  )
}
