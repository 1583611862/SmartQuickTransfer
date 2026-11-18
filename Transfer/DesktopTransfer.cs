using System;
using System.Collections.Generic;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.Transfer
{
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

            ContainerTransfer.Refresh(null, source);
            return moved;
        }

        private static List<GameInventory> BuildCandidateList(EmporiumEntry entry, GameItem item, bool fromMachine)
        {
            List<GameInventory> list = new List<GameInventory>();
            bool outside = MapUIManager.Instance != null && MapUIManager.Instance.isOutside;
            TransferSettings settings = ModEntry.Settings;
            string preference = fromMachine
                ? (settings == null ? TransferSettings.DesktopBackInventory : settings.MachineReturnTarget)
                : (settings == null ? TransferSettings.DesktopAuto : settings.DesktopTarget);

            if (preference == TransferSettings.DesktopPlayerInventory)
            {
                Add(list, entry.invElement);
                Add(list, entry.backInvinvElement);
            }
            else if (preference == TransferSettings.DesktopBackInventory)
            {
                Add(list, entry.backInvinvElement);
                Add(list, entry.invElement);
            }
            else if (preference == TransferSettings.DesktopFrontCounter)
            {
                Add(list, entry.frontInvinvElement);
                Add(list, entry.invElement);
            }
            else if (preference == TransferSettings.DesktopShowcase)
            {
                Add(list, entry.showcaseElement);
                Add(list, entry.invElement);
            }
            else
            {
                if (outside && !fromMachine)
                {
                    Add(list, entry.afterhourInventory);
                }
                Add(list, entry.invElement);
                Add(list, entry.backInvinvElement);
                Add(list, entry.frontInvinvElement);
                Add(list, entry.showcaseElement);
                if (!outside && !fromMachine)
                {
                    Add(list, entry.afterhourInventory);
                }
            }

            return list;
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
