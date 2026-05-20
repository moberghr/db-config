import type { ConfigEntry } from '@/api/entries'

/**
 * A node in the hierarchical key tree.
 *
 * Group nodes have `entry === null` and `children.length > 0`.
 * Leaf nodes have `entry !== null` and `children.length === 0`.
 *
 * Sort order within each level: groups (children.length > 0) before leaves,
 * then alphabetical by segment name within each category.
 */
export interface TreeNode {
  /** The key segment at this depth, e.g. 'Stripe' or 'Payment'. */
  segment: string
  /**
   * The full colon-joined prefix from the root down to (and including) this
   * node's segment, e.g. 'Stripe' or 'Stripe:Payment'.
   * For leaf nodes this equals the entry's full key.
   */
  fullPrefix: string
  /** Child group/leaf nodes, sorted: groups first then leaves, both alphabetical. */
  children: TreeNode[]
  /** Non-null for leaf nodes that carry a ConfigEntry. */
  entry: ConfigEntry | null
  /**
   * Total number of ConfigEntry leaves under this node (inclusive).
   * For a leaf node this is always 1.
   */
  descendantCount: number
}

/**
 * Build a hierarchical tree from a flat list of ConfigEntry objects.
 *
 * Each entry's key is split on ':' to determine its depth in the tree.
 * - 'SimpleKey' → a top-level leaf.
 * - 'Stripe:ApiKey' → group 'Stripe' → leaf 'ApiKey'.
 * - 'Stripe:Payment:ApiKey' → group 'Stripe' → group 'Payment' → leaf 'ApiKey'.
 *
 * Multiple entries that share the same scope can coexist in the same tree
 * because each entry is identified by its full composite key plus scope.
 * When two entries have different scopes but the same key path, they will
 * both appear as separate leaf nodes under the same parent groups (the group
 * nodes are deduplicated by fullPrefix only, so both leaves will be present
 * under the same group).
 */
export function buildTree(entries: ConfigEntry[]): TreeNode[] {
  return buildLevel(entries, [], 0)
}

/**
 * Recursively construct nodes for entries at the given depth.
 *
 * @param entries - entries still being partitioned at this level
 * @param prefixSegments - segments accumulated from the root to this call
 * @param depth - current depth (0 = root level)
 */
function buildLevel(
  entries: ConfigEntry[],
  prefixSegments: string[],
  depth: number,
): TreeNode[] {
  // Separate entries into:
  //   - leaves at this depth (no remaining segments after the current one)
  //   - entries that need to go deeper (grouped by their next segment)

  const leaves: TreeNode[] = []
  const groupMap = new Map<string, ConfigEntry[]>()

  for (const entry of entries) {
    const segments = entry.key.split(':')
    // The remaining segments relative to this depth
    const remaining = segments.slice(depth)

    if (remaining.length === 1) {
      // This entry is a leaf at the current level
      const segment = remaining[0]
      const fullPrefix = [...prefixSegments, segment].join(':')
      leaves.push({
        segment,
        fullPrefix,
        children: [],
        entry,
        descendantCount: 1,
      })
    } else {
      // This entry belongs to a child group
      const groupSegment = remaining[0]
      if (!groupMap.has(groupSegment)) {
        groupMap.set(groupSegment, [])
      }
      groupMap.get(groupSegment)!.push(entry)
    }
  }

  // Sort leaves alphabetically by segment
  leaves.sort((a, b) => a.segment.localeCompare(b.segment))

  // Build group nodes
  const groups: TreeNode[] = []
  for (const [segment, groupEntries] of groupMap) {
    const fullPrefix = [...prefixSegments, segment].join(':')
    const children = buildLevel(groupEntries, [...prefixSegments, segment], depth + 1)
    const descendantCount = children.reduce((sum, c) => sum + c.descendantCount, 0)
    groups.push({
      segment,
      fullPrefix,
      children,
      entry: null,
      descendantCount,
    })
  }

  // Sort groups alphabetically by segment
  groups.sort((a, b) => a.segment.localeCompare(b.segment))

  // Groups before leaves within each level
  return [...groups, ...leaves]
}
