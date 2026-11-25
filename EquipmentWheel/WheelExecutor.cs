using System;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// Executes an equipment change through the game's own entry point and verifies the result
    /// against real state. The vanilla path is ItemContextHandler's Equip button, which calls:
    ///
    ///     ItemSelectHandler.current.TryEquipItem(gameItem);
    ///
    /// TryEquipItem accepts an arbitrary GameItem - it does NOT require a Toolbox slot - and
    /// itself checks effective ownership, MaySelectSlotItem and CanSelectSlotItem. We therefore
    /// never move, swap or overwrite anything in the player's 1-9 Toolbox slots.
    /// </summary>
    internal static class WheelExecutor
    {
        internal static bool TryEquip(GameItem item)
        {
            if (item == null)
            {
                return false;
            }
            try
            {
                // Re-run the admission gate at confirm time: the item may have moved or changed.
                if (!WheelEligibility.IsToolboxAdmitted(item) || !WheelEligibility.CanEquipNow(item))
                {
                    return false;
                }

                ItemSelectHandler selector = ItemSelectHandler.current;
                if (selector == null)
                {
                    return false;
                }

                selector.TryEquipItem(item);

                // A missing exception is not success: verify the game's actual equipped reference.
                return ReferenceEquals(selector.GetEquippedItem(), item);
            }
            catch (Exception e)
            {
                ModEntry.LogError("equipment wheel: native equip failed: " + e.Message);
                return false;
            }
        }
    }
}
