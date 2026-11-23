using System;
using SmartQuickTransfer.Transfer;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Builds stable, persistent identity strings for items and destinations.
    ///
    /// Rules (from the task book):
    ///   - use the game's native identifier and Tag/enum information only;
    ///   - never use display names, window titles, runtime hashes, object references or screen
    ///     coordinates;
    ///   - distinguish variants of the same identifier where a stable native attribute exists.
    /// </summary>
    internal static class StableIdentityResolver
    {
        internal const string PrefixItem = "item:";
        internal const string PrefixMachine = "machine:";
        internal const string PrefixContainer = "container:";
        internal const string PrefixStorage = "storage:";
        internal const string PrefixUnknown = "unknown:";

        /// <summary>
        /// Stable identity of an item. The base is the native identifier. Where the identifier is
        /// empty but a game item type is available we fall back to it, otherwise to a neutral
        /// bucket. We deliberately do not append counters, unitCount or any runtime-only value
        /// that would prevent the same item kind from being recognised across sessions.
        /// </summary>
        internal static string ResolveItem(GameItem item)
        {
            if (item == null)
            {
                return PrefixUnknown + "null";
            }

            string id = item.identifier;
            if (!string.IsNullOrEmpty(id))
            {
                return PrefixItem + id;
            }

            try
            {
                if (item.IsTag(MachineHelper.STANDARD_MACHINE_TAG)
                    || item.IsTag(MachineHelper.MACHINE_RECHARGABLE_TAG))
                {
                    return PrefixItem + "machine";
                }
            }
            catch (Exception)
            {
                // Tag lookups must never break identity resolution.
            }

            return PrefixUnknown + "item";
        }

        /// <summary>
        /// Stable identity of a destination window/inventory. Machines resolve to their machine
        /// identifier; the fixed shop panels resolve to a stable storage: area key; other
        /// containers use their identifier. Window object references, titles and screen positions
        /// are never used.
        /// </summary>
        internal static string ResolveTarget(PixelWindow window, GameInventory inventory)
        {
            if (window == null)
            {
                return ResolveInventoryOnly(inventory);
            }

            // The fixed shop panels have a stable area key independent of the window object.
            string area = WindowActivityManager.DescribeShopArea(window);
            if (area != null)
            {
                return PrefixStorage + area;
            }

            GameItem owner = null;
            try
            {
                if (window.parentItems != null && window.parentItems.Count > 0)
                {
                    owner = window.parentItems[0];
                }
            }
            catch (Exception)
            {
                // The window may be mid-destruction.
            }

            if (owner != null && !string.IsNullOrEmpty(owner.identifier))
            {
                if (MachineSlotResolver.IsMachineItem(owner))
                {
                    return PrefixMachine + owner.identifier;
                }
                return PrefixContainer + owner.identifier;
            }

            // Fall back to the inventory identifier when the window owner cannot be read.
            return ResolveInventoryOnly(inventory);
        }

        /// <summary>Resolve a target key from an inventory alone (used by the manual-drag observer).</summary>
        internal static string ResolveInventoryOnly(GameInventory inventory)
        {
            if (inventory == null)
            {
                return PrefixUnknown + "target";
            }

            string area = DesktopTransfer.DescribeArea(inventory);
            if (!string.IsNullOrEmpty(area)
                && area != "其它容器"
                && area != "<none>")
            {
                return PrefixStorage + area;
            }

            if (!string.IsNullOrEmpty(inventory.identifier))
            {
                return PrefixContainer + inventory.identifier;
            }

            GameItem parent = null;
            try
            {
                parent = inventory.GetParentItem();
            }
            catch (Exception)
            {
                // ignore
            }
            if (parent != null && !string.IsNullOrEmpty(parent.identifier))
            {
                if (MachineSlotResolver.IsMachineItem(parent))
                {
                    return PrefixMachine + parent.identifier;
                }
                return PrefixContainer + parent.identifier;
            }

            return PrefixUnknown + "inventory";
        }
    }
}
