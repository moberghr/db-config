export function AccessWarningBanner() {
  return (
    <div className="rounded-md border border-yellow-200 bg-yellow-50 px-4 py-3 text-sm text-yellow-900 dark:border-yellow-800/50 dark:bg-yellow-900/30 dark:text-yellow-200">
      <span className="font-semibold">Warning:</span>{' '}
      Configuration values stored in this database may be visible to anyone with database access.
      Encryption at rest is the consumer&apos;s responsibility (DB TDE / column encryption).
    </div>
  )
}
