using System.Collections.Generic;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// Read-only scanner of the player's own storage. It walks the real inventory graph rather
    /// than the currently visible UI, so items on any page or inside owned containers are found.
    ///
    /// Roots are the EmporiumEntry grids that represent player storage. Store shelves that hold
    /// goods for sale (front counter / showcase), the trash, sold buffer and machine internals are
    /// intentionally not roots. Items are still filtered by the native owned + Toolbox predicate,
    /// so a non-owned item that shares a grid with the player's stock never enters the wheel.
    ///
    /// Eligible instances are grouped by identifier into ToolGroup entries: identical tools show
    /// as ONE sector with a usable count, while every distinct instance is retained for execution.
    /// </summary>
    internal sealed class WheelScanner
    {
        private const int MaxDepth = 16;
        private const int MaxInstances = 256;
        private const int MaxResults = 128;

        internal static List<ToolGroup> Scan(bool includeNested)
        {
            List<GameItem> found = new List<GameItem>();
            EmporiumEntry emporium = EmporiumEntry.Instance;
            if (emporium == null)
            {
                return new List<ToolGroup>();
            }

            // Only player-owned storage areas. Pagination is a UI concern: each of these grids is
            // a distinct underlying GameGridInventory, and every cell in each one is visited.
            GameInventory[] roots = new GameInventory[]
            {
                emporium.invElement,               // 仓库: main 24x10 grid
                emporium.backInvinvElement,        // 桌面: back counter grid
                emporium.backInvinvElementCounter, // 桌面: secondary counter grid
                emporium.afterhourInventory,       // 夜间地面: after-hours drop grid
                emporium.hirelingInv               // 雇员装备
            };

            HashSet<GraphNodeStorage> visitedNodes = new HashSet<GraphNodeStorage>();
            HashSet<GameItem> visitedReferences = new HashSet<GameItem>();
            HashSet<int> visitedIds = new HashSet<int>();

            for (int i = 0; i < roots.Length; i++)
            {
                Walk(roots[i], 0, includeNested, visitedNodes, visitedReferences, visitedIds, found);
            }

            // Group by identifier: same tool -> one sector with a count. Instance-level de-dup
            // (reference + uniqueId) already happened in Walk, so one physical item counts once.
            List<ToolGroup> groups = new List<ToolGroup>();
            Dictionary<string, ToolGroup> byId = new Dictionary<string, ToolGroup>();
            for (int i = 0; i < found.Count && groups.Count < MaxResults; i++)
            {
                GameItem item = found[i];
                string key = item.identifier ?? string.Empty;
                ToolGroup group;
                if (!byId.TryGetValue(key, out group))
                {
                    group = new ToolGroup(key);
                    byId.Add(key, group);
                    groups.Add(group);
                }
                group.Add(item);
            }
            return groups;
        }

        private static void Walk(GraphNodeStorage node, int depth, bool includeNested,
            HashSet<GraphNodeStorage> visitedNodes, HashSet<GameItem> visitedReferences,
            HashSet<int> visitedIds, List<GameItem> result)
        {
            if (node == null || depth > MaxDepth || result.Count >= MaxInstances)
            {
                return;
            }
            // GraphUtils.FindAllChildrenType has no visited set; keep our own to break cycles and
            // to avoid walking the same storage twice when two roots share a subtree.
            if (!visitedNodes.Add(node))
            {
                return;
            }

            GameItem item = node as GameItem;
            if (item != null)
            {
                if (WheelEligibility.IsToolboxAdmitted(item))
                {
                    // Reference identity + uniqueId suppress the same physical instance being
                    // counted twice; two distinct copies of one tool still both get counted.
                    bool newReference = visitedReferences.Add(item);
                    int id = item.uniqueId;
                    bool newId = id <= 0 || visitedIds.Add(id);
                    if (newReference && newId)
                    {
                        result.Add(item);
                    }
                }

                // Descend only into the item's own content window (backpacks, boxes, toolboxes).
                // Never follow examineWindow: inspection views can display non-player items.
                if (includeNested && item.contentWindow != null)
                {
                    Walk(item.contentWindow, depth + 1, includeNested, visitedNodes,
                        visitedReferences, visitedIds, result);
                }
                return;
            }

            GameInventory inventory = node as GameInventory;
            if (inventory != null)
            {
                List<GameItem> children = inventory.childItems;
                if (children == null)
                {
                    return;
                }
                for (int i = 0; i < children.Count; i++)
                {
                    Walk(children[i], depth + 1, includeNested, visitedNodes,
                        visitedReferences, visitedIds, result);
                }
                return;
            }

            // Window / panel node: its graph children lead into the inventories it hosts.
            List<GraphNodeStorage> nodes = node.children;
            if (nodes == null)
            {
                return;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                Walk(nodes[i], depth + 1, includeNested, visitedNodes,
                    visitedReferences, visitedIds, result);
            }
        }
    }
}
