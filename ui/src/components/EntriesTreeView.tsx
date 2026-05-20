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
import { Copy, History, Pencil, Trash2, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'

const CROSS_SCOPE_TITLE =
  'Cross-scope edits are not allowed in this UI. Switch to that scope or use a host with platform-admin access.'

// Per-depth padding step. 28px reads more clearly than 20 at typical table
// densities. Paired with a left border guide on the indented `<span>` for an
// explicit hierarchy cue (see TableCell below).
const INDENT_PX = 28

function compositeKey(entry: ConfigEntry): string {
  return `${entry.scope}|${entry.environment}|${entry.tenantId}|${entry.key}`
}

interface EntriesTreeViewProps {
  onEdit: (entry: ConfigEntry) => void
  onDelete: (entry: ConfigEntry) => void
  onHistory: (entry: ConfigEntry) => void
  onDuplicate: (entry: ConfigEntry) => void
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
  onDuplicate: (entry: ConfigEntry) => void
  selectedKeys: Set<string>
  toggleSelection: (ck: string) => void
  currentScope: string
}

/**
 * Render a left-side indent guide for a row at the given depth.
 * Each depth level draws a thin vertical bar so nesting is unambiguous even
 * when ancestor segments are off-screen.
 */
/**
 * Per-depth left padding for tree rows. Applied via inline style to the row's
 * first cell so the cell's `paddingLeft` actually shifts the content right —
 * cleaner than trying to expand a cell beyond the column's shared width.
 *
 * The vertical guide lines are painted into the padding via a repeating
 * background gradient so each ancestor depth gets a faint border-l hint.
 */
/**
 * Per-depth left padding for the row's first cell.
 *
 * `depth * 28 + 28` gives EVERY row (including depth 0) a baseline 28px of left
 * padding that aligns its content with depth-0 group labels in column 2. Each
 * additional depth level adds another 28px step. Guide lines are painted into
 * the padding via a repeating background gradient so ancestor depth is visible.
 */
function leafIndentStyle(depth: number): React.CSSProperties {
  const totalPaddingPx = (depth + 1) * INDENT_PX
  return {
    paddingLeft: `${totalPaddingPx}px`,
    backgroundImage: `repeating-linear-gradient(to right, transparent 0, transparent ${INDENT_PX - 1}px, var(--border) ${INDENT_PX - 1}px, var(--border) ${INDENT_PX}px)`,
    backgroundSize: `${depth * INDENT_PX}px 100%`,
    backgroundRepeat: 'no-repeat',
    backgroundPosition: 'left center',
  }
}

function TreeRows({
  nodes,
  depth,
  expandedPrefixes,
  onToggle,
  onEdit,
  onDelete,
  onHistory,
  onDuplicate,
  selectedKeys,
  toggleSelection,
  currentScope,
}: TreeRowsProps) {
  return (
    <>
      {nodes.map((node) => {
        if (node.entry !== null) {
          // Leaf row — render like EntriesTable row
          const entry = node.entry
          const isOwn = !currentScope || entry.scope === currentScope
          const ck = compositeKey(entry)
          const isSelected = selectedKeys.has(ck)

          return (
            <TableRow
              key={`leaf-${ck}`}
              className={cn('cursor-pointer', !isOwn && 'opacity-80', isSelected && 'bg-primary/5')}
              onClick={() => { if (isOwn) onEdit(entry) }}
            >
              {/* Checkbox — paddingLeft on the cell itself indents the checkbox per depth.
                  Indent guide painted into the same padding via background-image so the
                  hierarchy is visible. */}
              <TableCell
                onClick={(e) => e.stopPropagation()}
                style={leafIndentStyle(depth)}
              >
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded border border-input accent-primary"
                  checked={isSelected}
                  onChange={() => toggleSelection(ck)}
                  aria-label={`Select ${entry.key}`}
                />
              </TableCell>
              {/* Key — chevron-width spacer so leaf text aligns with group labels */}
              <TableCell className="font-mono text-xs font-medium">
                <span className="inline-flex items-center gap-1.5">
                  <span className="inline-block h-4 w-4 shrink-0" aria-hidden />
                  <span>{node.segment}</span>
                </span>
              </TableCell>
              {/* Value */}
              <TableCell>
                <SecretValueCell value={entry.value} isSecret={entry.isSecret} />
              </TableCell>
              {/* Scope */}
              <TableCell>
                <span className="text-xs text-foreground">{entry.scope}</span>
              </TableCell>
              {/* Environment */}
              <TableCell>
                <span className="text-xs text-foreground">{entry.environment}</span>
              </TableCell>
              {/* Tenant */}
              <TableCell>
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
                    title="Duplicate — opens a Create dialog pre-filled from this entry"
                    onClick={() => onDuplicate(entry)}
                  >
                    <Copy className="h-3.5 w-3.5" />
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
              {/* Group rows merge cells so the chevron sits at the same paddingLeft as
                  a leaf's checkbox (which is in column 1). Otherwise the chevron lands
                  inside column 2 and never aligns with the leaf checkbox column. */}
              <TableCell colSpan={9} style={leafIndentStyle(depth)}>
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
                onDuplicate={onDuplicate}
                selectedKeys={selectedKeys}
                toggleSelection={toggleSelection}
                currentScope={currentScope}
              />
            )}
          </>
        )
      })}
    </>
  )
}

export function EntriesTreeView({ onEdit, onDelete, onHistory, onDuplicate, visibleEntries }: EntriesTreeViewProps) {
  const loading = useEntriesStore((s) => s.loading)
  const error = useEntriesStore((s) => s.error)
  const selectedKeys = useEntriesStore((s) => s.selectedKeys)
  const toggleSelection = useEntriesStore((s) => s.toggleSelection)
  const currentScope = useScopeStore((s) => s.scope)

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
            <TableHead>Scope</TableHead>
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
            onDuplicate={onDuplicate}
            selectedKeys={selectedKeys}
            toggleSelection={toggleSelection}
            currentScope={currentScope}
          />
        </TableBody>
      </Table>
    </div>
  )
}
