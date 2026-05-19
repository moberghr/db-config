import { useMemo, useState } from 'react'
import type { ConfigEntry } from '@/api/entries'
import { buildTree, type TreeNode } from '@/lib/keyTree'
import { useEntriesStore } from '@/store/entriesStore'
import { useScopeStore } from '@/store/scopeStore'
import { SecretValueCell } from './SecretValueCell'
import { Button } from '@/components/ui/button'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table'
import { History, Pencil, Trash2, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'

const CROSS_SCOPE_TITLE =
  'Cross-scope edits are not allowed in this UI. Switch to that scope or use a host with platform-admin access.'

// Per-depth padding step. 28px reads more clearly than 20 at typical table
// densities. Paired with a left border guide on the indented `<span>` for an
// explicit hierarchy cue (see TableCell below).
const INDENT_PX = 28

function compositeKey(entry: ConfigEntry): string {
  return `${entry.appName}|${entry.environment}|${entry.tenantId}|${entry.key}`
}

interface EntriesTreeViewProps {
  onEdit: (entry: ConfigEntry) => void
  onDelete: (entry: ConfigEntry) => void
  onHistory: (entry: ConfigEntry) => void
  visibleEntries: ConfigEntry[]
}

/**
 * Collects all fullPrefix values from a list of tree nodes (recursively),
 * used for "Expand all".
 */
function collectAllPrefixes(nodes: TreeNode[]): string[] {
  const prefixes: string[] = []
  for (const node of nodes) {
    if (node.children.length > 0) {
      prefixes.push(node.fullPrefix)
      prefixes.push(...collectAllPrefixes(node.children))
    }
  }
  return prefixes
}

interface TreeRowsProps {
  nodes: TreeNode[]
  depth: number
  expandedPrefixes: Set<string>
  onToggle: (fullPrefix: string) => void
  onEdit: (entry: ConfigEntry) => void
  onDelete: (entry: ConfigEntry) => void
  onHistory: (entry: ConfigEntry) => void
  selectedKeys: Set<string>
  toggleSelection: (ck: string) => void
  currentAppName: string
}

/**
 * Render a left-side indent guide for a row at the given depth.
 * Each depth level draws a thin vertical bar so nesting is unambiguous even
 * when ancestor segments are off-screen.
 */
function IndentGuide({ depth }: { depth: number }) {
  if (depth === 0) {
    return null
  }
  return (
    <span
      aria-hidden
      className="inline-flex items-stretch"
      style={{ width: `${depth * INDENT_PX}px` }}
    >
      {Array.from({ length: depth }, (_, i) => (
        <span
          key={i}
          className="block w-0 border-l border-border/60"
          style={{ width: `${INDENT_PX}px` }}
        />
      ))}
    </span>
  )
}

function TreeRows({
  nodes,
  depth,
  expandedPrefixes,
  onToggle,
  onEdit,
  onDelete,
  onHistory,
  selectedKeys,
  toggleSelection,
  currentAppName,
}: TreeRowsProps) {
  return (
    <>
      {nodes.map((node) => {
        if (node.entry !== null) {
          // Leaf row — render like EntriesTable row
          const entry = node.entry
          const isOwn = !currentAppName || entry.appName === currentAppName
          const ck = compositeKey(entry)
          const isSelected = selectedKeys.has(ck)

          return (
            <TableRow
              key={`leaf-${ck}`}
              className={cn('cursor-pointer', !isOwn && 'opacity-80', isSelected && 'bg-primary/5')}
              onClick={() => { if (isOwn) onEdit(entry) }}
            >
              {/* Checkbox — preceded by indent guide so the whole row reads as nested */}
              <TableCell onClick={(e) => e.stopPropagation()}>
                <span className="inline-flex items-center">
                  <IndentGuide depth={depth} />
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border border-input accent-primary"
                    checked={isSelected}
                    onChange={() => toggleSelection(ck)}
                    aria-label={`Select ${entry.key}`}
                  />
                </span>
              </TableCell>
              {/* Key */}
              <TableCell className="font-mono text-xs font-medium">
                {node.segment}
              </TableCell>
              {/* Value */}
              <TableCell onClick={(e) => e.stopPropagation()}>
                <SecretValueCell value={entry.value} isSecret={entry.isSecret} />
              </TableCell>
              {/* AppName */}
              <TableCell onClick={(e) => e.stopPropagation()}>
                <span className="text-xs text-foreground">{entry.appName}</span>
              </TableCell>
              {/* Environment */}
              <TableCell onClick={(e) => e.stopPropagation()}>
                <span className="text-xs text-foreground">{entry.environment}</span>
              </TableCell>
              {/* Tenant */}
              <TableCell onClick={(e) => e.stopPropagation()}>
                {entry.tenantId ? (
                  <span className="text-xs text-foreground">{entry.tenantId}</span>
                ) : (
                  <span className="text-xs text-muted-foreground italic">default</span>
                )}
              </TableCell>
              {/* Modified */}
              <TableCell className="text-muted-foreground text-xs">
                {new Date(entry.modifiedUtc).toLocaleString()}
              </TableCell>
              {/* Modified By */}
              <TableCell className="text-muted-foreground text-xs">
                {entry.modifiedBy ?? '—'}
              </TableCell>
              {/* Actions */}
              <TableCell className="text-right" onClick={(e) => e.stopPropagation()}>
                <span className="flex items-center justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7"
                    title="History"
                    onClick={() => onHistory(entry)}
                  >
                    <History className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7"
                    title={isOwn ? 'Edit' : CROSS_SCOPE_TITLE}
                    disabled={!isOwn}
                    onClick={() => { if (isOwn) onEdit(entry) }}
                  >
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7 text-destructive hover:text-destructive"
                    title={isOwn ? 'Delete' : CROSS_SCOPE_TITLE}
                    disabled={!isOwn}
                    onClick={() => { if (isOwn) onDelete(entry) }}
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </span>
              </TableCell>
            </TableRow>
          )
        }

        // Group row
        const isExpanded = expandedPrefixes.has(node.fullPrefix)

        return (
          <>
            <TableRow
              key={`group-${node.fullPrefix}`}
              className="cursor-pointer hover:bg-muted/50 select-none"
              onClick={() => onToggle(node.fullPrefix)}
            >
              {/* Checkbox placeholder — render indent guide so group rows nest visually */}
              <TableCell>
                <IndentGuide depth={depth} />
              </TableCell>
              {/* Group label with chevron */}
              <TableCell colSpan={7}>
                <span className="inline-flex items-center gap-1.5 font-medium text-sm">
                  <ChevronRight
                    className={cn(
                      'h-4 w-4 text-muted-foreground transition-transform duration-150',
                      isExpanded && 'rotate-90'
                    )}
                  />
                  <span>{node.segment}</span>
                  <span className="ml-1 rounded-full bg-muted px-1.5 py-0.5 text-xs text-muted-foreground font-normal">
                    {node.descendantCount}
                  </span>
                </span>
              </TableCell>
              {/* Actions placeholder */}
              <TableCell />
            </TableRow>
            {isExpanded && (
              <TreeRows
                nodes={node.children}
                depth={depth + 1}
                expandedPrefixes={expandedPrefixes}
                onToggle={onToggle}
                onEdit={onEdit}
                onDelete={onDelete}
                onHistory={onHistory}
                selectedKeys={selectedKeys}
                toggleSelection={toggleSelection}
                currentAppName={currentAppName}
              />
            )}
          </>
        )
      })}
    </>
  )
}

export function EntriesTreeView({ onEdit, onDelete, onHistory, visibleEntries }: EntriesTreeViewProps) {
  const loading = useEntriesStore((s) => s.loading)
  const error = useEntriesStore((s) => s.error)
  const selectedKeys = useEntriesStore((s) => s.selectedKeys)
  const toggleSelection = useEntriesStore((s) => s.toggleSelection)
  const currentAppName = useScopeStore((s) => s.appName)

  const [expandedPrefixes, setExpandedPrefixes] = useState<Set<string>>(new Set())

  const tree = useMemo(() => buildTree(visibleEntries), [visibleEntries])

  const allGroupPrefixes = useMemo(() => collectAllPrefixes(tree), [tree])

  function handleToggle(fullPrefix: string) {
    setExpandedPrefixes((prev) => {
      const next = new Set(prev)
      if (next.has(fullPrefix)) {
        next.delete(fullPrefix)
      } else {
        next.add(fullPrefix)
      }
      return next
    })
  }

  function expandAll() {
    setExpandedPrefixes(new Set(allGroupPrefixes))
  }

  function collapseAll() {
    setExpandedPrefixes(new Set())
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-16 text-muted-foreground text-sm">
        Loading entries…
      </div>
    )
  }

  if (error) {
    return (
      <div className="rounded-md border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive">
        {error}
      </div>
    )
  }

  if (visibleEntries.length === 0) {
    return (
      <div className="flex items-center justify-center py-16 text-muted-foreground text-sm">
        No entries found. Use the &ldquo;New Entry&rdquo; button to add one.
      </div>
    )
  }

  return (
    <div className="space-y-2">
      {/* Expand / Collapse all controls */}
      <div className="flex items-center gap-2 px-1">
        <button
          type="button"
          onClick={expandAll}
          className="text-xs text-muted-foreground hover:text-foreground underline underline-offset-2 transition-colors"
        >
          Expand all
        </button>
        <span className="text-muted-foreground text-xs">·</span>
        <button
          type="button"
          onClick={collapseAll}
          className="text-xs text-muted-foreground hover:text-foreground underline underline-offset-2 transition-colors"
        >
          Collapse all
        </button>
      </div>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-10" />
            <TableHead>Key</TableHead>
            <TableHead>Value</TableHead>
            <TableHead>AppName</TableHead>
            <TableHead>Environment</TableHead>
            <TableHead>Tenant</TableHead>
            <TableHead>Modified</TableHead>
            <TableHead>Modified By</TableHead>
            <TableHead className="w-28 text-right">Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TreeRows
            nodes={tree}
            depth={0}
            expandedPrefixes={expandedPrefixes}
            onToggle={handleToggle}
            onEdit={onEdit}
            onDelete={onDelete}
            onHistory={onHistory}
            selectedKeys={selectedKeys}
            toggleSelection={toggleSelection}
            currentAppName={currentAppName}
          />
        </TableBody>
      </Table>
    </div>
  )
}
