import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { queryEntries } from '@/api/entries'
import { useScopeStore } from '@/store/scopeStore'
import { flatToNested } from '@/lib/appsettings'
import { Download } from 'lucide-react'

export function ExportButton() {
  const scope = useScopeStore((s) => s.scope)
  const environment = useScopeStore((s) => s.environment)
  const [exporting, setExporting] = useState(false)

  async function handleExport() {
    if (!scope || !environment) return
    setExporting(true)
    try {
      const entries = await queryEntries({ scope, environment, tenantId: '' })

      const { config, metadata } = flatToNested(entries)

      // Guard against a user entry literally named '_dbconfig', which would be silently
      // overwritten by the metadata sidecar. Explicit failure is better than silent data loss.
      if ('_dbconfig' in config) {
        throw new Error(
          "Cannot export — your config contains a top-level key '_dbconfig' which collides " +
          "with the metadata sidecar namespace. Rename the key or export without metadata."
        )
      }

      const merged = { ...config, ...metadata }
      const json = JSON.stringify(merged, null, 2)

      const blob = new Blob([json], { type: 'application/json' })
      const url = URL.createObjectURL(blob)

      const isoDate = new Date().toISOString().slice(0, 10)
      const safeName = (s: string) => s.replace(/[^a-zA-Z0-9-_]/g, '_')
      const filename = `dbconfig-${safeName(scope)}-${safeName(environment)}-${isoDate}.json`

      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = filename
      anchor.click()
      URL.revokeObjectURL(url)
    } finally {
      setExporting(false)
    }
  }

  return (
    <Button
      size="sm"
      variant="outline"
      className="gap-1.5"
      disabled={!scope || !environment || exporting}
      onClick={() => { void handleExport() }}
    >
      <Download className="h-3.5 w-3.5" />
      {exporting ? 'Exporting…' : 'Export'}
    </Button>
  )
}
