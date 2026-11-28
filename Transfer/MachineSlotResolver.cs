using System;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.Transfer
{
    internal static class MachineSlotResolver
    {
        internal static SlotMarker FindValidSlot(PixelWindow window, GameItem item)
        {
            if (window == null || item == null)
            {
                return null;
            }

            try
            {
                SlotMarker slot = window.TryFindOneValidInventorySlot(item);
                if (slot != null && slot.IsValid())
                {
                    return slot;
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("native slot validation failed on '" + WindowActivityManager.Describe(window) + "': " + e);
            }

            return null;
        }

        internal static bool IsMachineryWindow(PixelWindow window)
        {
            if (window == null)
            {
                return false;
            }

            try
            {
                if (window.style == WindowStyle.Machinery)
                {
                    return true;
                }
                return window.childElement is MachineryPanelRow;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// A machine can be recognised before its window exists: the tag is set when the item is
        /// built, while contentWindow may still be null or may be a plain window. Checking both
        /// keeps the base game's double-click working for every machine.
        /// </summary>
        internal static bool IsMachineItem(GameItem item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                if (item.IsTag(MachineHelper.STANDARD_MACHINE_TAG))
                {
                    return true;
                }
                if (item.IsTag(MachineHelper.MACHINE_RECHARGABLE_TAG))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Tag lookup must never break the double-click path.
            }

            return IsMachineryWindow(item.contentWindow);
        }

        /// <summary>
        /// True when the window belongs to a processing machine (furnace, blender, purifier, ...).
        /// This is the native counterpart of a storage container: processing machines carry the
        /// game's MACHINE item type and/or the standard machine tags, while storage containers
        /// (smuggler bays, storage bays, toolboxes, boxes of every form) carry STORAGE/CONTAINER
        /// types instead. Used to keep machine-sourced smart transfers between containers and
        /// away from processing machines. Relies only on native type/tag data - never on a window
        /// title, display name or a hard-coded item identifier.
        /// </summary>
        internal static bool IsProcessingMachineWindow(PixelWindow window)
        {
            if (window == null)
            {
                return false;
            }

            try
            {
                if (window.parentItems == null || window.parentItems.Count == 0)
                {
                    return false;
                }
                GameItem owner = window.parentItems[0];
                if (owner == null)
                {
                    return false;
                }
                if (owner.IsGameItemType("MACHINE"))
                {
                    return true;
                }
                if (owner.IsTag(MachineHelper.STANDARD_MACHINE_TAG)
                    || owner.IsTag(MachineHelper.MACHINE_RECHARGABLE_TAG))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // An owner may be mid-destruction; conservative false keeps normal routing.
            }

            return false;
        }

        internal static string Describe(PixelWindow window)
        {
            if (window == null)
            {
                return "<none>";
            }

            if (IsMachineryWindow(window))
            {
                return "machinery/tool window";
            }
            return "container window";
        }
    }
}
