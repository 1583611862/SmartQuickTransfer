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
