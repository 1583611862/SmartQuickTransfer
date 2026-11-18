using System;
using System.Collections.Generic;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.Transfer
{
    /// <summary>
    /// Fallback routing into the fixed shop panels. The panel identities follow the on-screen
    /// layout, not the internal field names:
    ///   仓库     = EmporiumEntry.invElement          (bottom 24x10 grid, the game's "inventory")
    ///   桌面     = EmporiumEntry.backInvinvElement   (middle 18x4 counter, the game's "counter")
    ///   展示柜   = EmporiumEntry.showcaseElement
    ///   前台柜台 = EmporiumEntry.frontInvinvElement
    ///   夜间地面 = EmporiumEntry.afterhourInventory
    /// </summary>
    internal static class DesktopTransfer
    {
        internal static GameInventory Resolve(GameItem item, PixelWindow sourceWindow, bool fromMachine)
        {
            if (item == null)
            {
                return null;
            }

            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null)
            {
                return null;
            }

            List<GameInventory> candidates = BuildCandidateList(entry, item, fromMachine);
            for (int i = 0; i < candidates.Count; i++)
            {
                GameInventory candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }
                if (candidate == item.parentInventory)
                {
                    continue;
                }
                if (IsInsideItem(candidate, item))
                {
                    continue;
                }

                try
                {
                    if (candidate.TryFindOneValidInventorySlot(item) != null)
                    {
                        return candidate;
                    }
                }
                catch (Exception e)
                {
                    ModEntry.LogError("fallback storage check failed: " + e.Message);
                }
            }

            return null;
        }

        internal static int MoveInto(GameInventory destination, GameItem item, PixelWindow source)
        {
            if (destination == null || item == null)
            {
                return 0;
            }

            int requested = item.unitCount;
            if (requested <= 0)
            {
                return 0;
            }

            int leftover = GraphUtils.TryAcceptAll(destination, item);
            int moved = requested - leftover;

            try
            {
                item.Validate();
            }
            catch (Exception e)
            {
                ModEntry.LogError("validating the moved item failed: " + e.Message);
            }

            // Refresh the window that owns the destination as well, otherwise the icon keeps its
            // old anchored position for a frame and the flight animation aims at the wrong place.
            ContainerTransfer.Refresh(OwnerWindow(destination), source);
            return moved;
        }

        internal static string DescribeArea(GameInventory inventory)
        {
            if (inventory == null)
            {
                return "<none>";
            }

            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry != null)
            {
                if (ReferenceEquals(inventory, entry.invElement))
                {
                    return TransferSettings.TargetStorage;
                }
                if (ReferenceEquals(inventory, entry.backInvinvElement))
                {
                    return TransferSettings.TargetDesk;
                }
                if (ReferenceEquals(inventory, entry.showcaseElement))
                {
                    return TransferSettings.TargetShowcase;
                }
                if (ReferenceEquals(inventory, entry.frontInvinvElement))
                {
                    return TransferSettings.TargetFrontCounter;
                }
                if (ReferenceEquals(inventory, entry.afterhourInventory))
                {
                    return TransferSettings.TargetNightGround;
                }
            }

            return string.IsNullOrEmpty(inventory.identifier) ? "其它容器" : inventory.identifier;
        }

        private static List<GameInventory> BuildCandidateList(EmporiumEntry entry, GameItem item, bool fromMachine)
        {
            List<GameInventory> list = new List<GameInventory>();
            bool outside = MapUIManager.Instance != null && MapUIManager.Instance.isOutside;
            TransferSettings settings = ModEntry.Settings;
            string preference = fromMachine
                ? (settings == null ? TransferSettings.TargetStorage : settings.MachineOutputArea)
                : (settings == null ? TransferSettings.TargetAuto : settings.FallbackArea);

            if (preference != TransferSettings.TargetAuto)
            {
                Add(list, InventoryFor(entry, preference));
                Add(list, entry.invElement);
                Add(list, entry.backInvinvElement);
                return list;
            }

            // Outside (night ground, raids) the ground panel is where the player expects a
            // double-clicked item to land; inside the shop the storage grid comes first, exactly
            // like the game's own quick-move to the inventory.
            if (!fromMachine && outside)
            {
                Add(list, entry.afterhourInventory);
            }
            Add(list, entry.invElement);
            Add(list, entry.backInvinvElement);
            Add(list, entry.showcaseElement);
            Add(list, entry.frontInvinvElement);
            if (!fromMachine)
            {
                Add(list, entry.afterhourInventory);
            }

            return list;
        }

        private static GameInventory InventoryFor(EmporiumEntry entry, string target)
        {
            if (target == TransferSettings.TargetStorage)
            {
                return entry.invElement;
            }
            if (target == TransferSettings.TargetDesk)
            {
                return entry.backInvinvElement;
            }
            if (target == TransferSettings.TargetShowcase)
            {
                return entry.showcaseElement;
            }
            if (target == TransferSettings.TargetFrontCounter)
            {
                return entry.frontInvinvElement;
            }
            if (target == TransferSettings.TargetNightGround)
            {
                return entry.afterhourInventory;
            }
            return null;
        }

        private static PixelWindow OwnerWindow(GameInventory inventory)
        {
            if (inventory == null)
            {
                return null;
            }

            try
            {
                return GraphUtils.FindParentType<PixelWindow>(inventory);
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not resolve the destination window: " + e.Message);
                return null;
            }
        }

        private static void Add(List<GameInventory> list, GameInventory inventory)
        {
            if (inventory != null && !list.Contains(inventory))
            {
                list.Add(inventory);
            }
        }

        private static bool IsInsideItem(GameInventory inventory, GameItem item)
        {
            try
            {
                return GraphUtils.FindParentEquals(inventory, item);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
